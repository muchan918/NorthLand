using System.Collections.Generic;
using NorthLand.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

/// 주민을 들어서 끌고 다니다 생산 건물에 떨어뜨려 배치하는 경로(Docs/ManagementArea/Resident.md §8).
/// `ResidentSelectionCoordinator`의 형제다 — MouseManager 입력을 받아 도메인으로 번역하는 같은 자리·같은 규약.
///
/// ── 지금 들어간 범위 ────────────────────────────────────────────────
///
/// | 조작 | 지금 일어나는 일 |
/// |---|---|
/// | 주민을 눌러 끌기 시작 | 공중으로 들려 **탑처럼 쌓인 채 커서를 따라온다**(앉은 자세) — `ResidentCarryVisual` |
/// | 생산 건물에 놓기 | `AssignVillager`로 배치. 성공한 인원은 그 자리에서 소멸(뿅) |
/// | 바닥·그 밖에 놓기 | 탑이 **터지면서 커서 주변 NavMesh 위로 흩어져 착지**한다 |
///
/// 어지러움(R11) · 거절 피드백은 아직 없다. 배치 판정과 인원 회계는 연출과 무관하게 그대로다.
///
/// **연출은 이 클래스가 하지 않는다.** 여기는 "누구를 들었는가 / 어디에 배치되는가"만 소유하고,
/// 몸을 어디에 그릴지는 <see cref="ResidentCarryVisual"/>이 맡는다. 착지가 끝나면 그쪽이
/// <see cref="ResidentCarryVisual.OnLanded"/>로 알려 오고, **되돌리는 호출은 여기서만 한다** —
/// 연출이 스포너를 직접 부르면 인원 회계의 창구가 둘로 갈린다.
///
/// ── 경계 ────────────────────────────────────────────────────────────
///
/// - **배치 수의 임자는 `ManagementController`다**(§1). 드롭 배치도 예외 없이 `AssignVillager` 게이트웨이를
///   통과한다. 여기서 상한·밤을 다시 판정하지 않는 이유가 그것이다 — 게이트웨이가 이미 판정하고 bool을 준다.
///   같은 조건을 두 곳에 두면 조용히 어긋난다(§3.2).
/// - **화면에서 감추고 되돌리는 것은 `ResidentSpawner`가 한다.** 풀의 불변식(누가 재사용 가능한가)이 거기
///   있기 때문이다. 이 클래스는 "누구를 들고 있는가"라는 목록 하나만 소유한다.
/// - **선택 집합은 `ResidentSelectionCoordinator`가 계속 소유한다.** 여기서는 읽기만 한다.
///
/// ── 씬을 건드리지 않는다 ────────────────────────────────────────────
///
/// `ResidentSelectionCoordinator`와 같은 이유로 런타임에 스스로 부팅하고 참조도 런타임에 찾는다
/// (정본 씬 병합 규칙 `Docs/Core/SceneWorkflow.md`와의 충돌 방지).
[DisallowMultipleComponent]
public class ResidentDragCoordinator : MonoBehaviour
{
    /// 참조를 못 찾았을 때 다시 찾기까지 쉬는 프레임 수(`ResidentSelectionCoordinator`와 같은 값·같은 이유).
    private const int k_RetryFrames = 120;

    private static ResidentDragCoordinator s_instance;
    public static ResidentDragCoordinator Instance => s_instance;

    /// 지금 들고 있는 주민. 놓는 순간 전원이 배치되거나 되돌아가므로 드래그 1회보다 오래 살지 않는다.
    private readonly List<Resident> _carried = new();

    /// 무엇을 들지 고르는 작업 버퍼. 매 드래그 배열을 새로 만들지 않기 위한 것.
    private readonly List<ResidentSelectable> _pickBuffer = new();

    /// 배치되지 못해 바닥으로 떨어질 인원. **한 번에 모아서** 연출에 넘긴다 —
    /// 착지 지점을 고르게 흩으려면 몇 명인지를 알아야 한다.
    private readonly List<Resident> _dropBuffer = new();

    /// 들린 몸을 그리는 쪽. 같은 오브젝트에 붙어 수명을 함께한다(낙하는 드래그가 끝난 뒤에도 이어진다).
    private ResidentCarryVisual _visual;

    private ManagementController _management;
    private ResidentSpawner _spawner;

    private bool _subscribed;
    private bool _dayNightSubscribed;
    private int _retryCountdown;
    private bool _warnedNoMouseManager;
    private bool _warnedNoSpawner;

    /// 지금 들고 있는 인원. 이후 커서 옆 표시나 거절 피드백이 붙을 자리다.
    public int CarriedCount => _carried.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;

        var go = new GameObject(nameof(ResidentDragCoordinator));
        s_instance = go.AddComponent<ResidentDragCoordinator>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(this);
            return;
        }
        s_instance = this;

        // 연출을 같은 오브젝트에 얹는다 — 수명이 같고(드래그가 끝난 뒤에도 낙하가 이어진다),
        // 씬을 건드리지 않는다는 이 클래스의 규칙도 그대로 따른다. 덤으로 플레이 중 인스펙터에서
        // 부양 높이·낙하를 만질 수 있다(눈으로 맞추는 수치라 상수로 박으면 매번 도메인 리로드다).
        _visual = GetComponent<ResidentCarryVisual>();
        if (_visual == null) _visual = gameObject.AddComponent<ResidentCarryVisual>();

        _visual.OnLanded += HandleLanded;
    }

    private void Start()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TrySubscribe();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        // MouseManager는 DontDestroyOnLoad라 이쪽보다 오래 살 수 있다 → 반드시 해제.
        // DayNightManager는 **씬 싱글톤**이라 보통 먼저 죽지만, 살아 있는 경우가 남으므로 같이 푼다.
        var mm = MouseManager.Instance;
        if (mm != null && _subscribed)
        {
            mm.OnUnitDragBegin -= HandleUnitDragBegin;
            mm.OnUnitDragEnd -= HandleUnitDragEnd;
        }

        var dayNight = DayNightManager.Instance;
        if (dayNight != null && _dayNightSubscribed) dayNight.OnDayToNight -= HandleDayToNight;

        if (_visual != null) _visual.OnLanded -= HandleLanded;

        if (s_instance == this) s_instance = null;
    }

    private void LateUpdate()
    {
        // 어느 쪽도 씬 배선이 아니라 런타임 탐색이라, 뒤늦게 등장할 수 있다 → 붙을 때까지 확인한다.
        if (!_subscribed) TrySubscribe();
        if (!_dayNightSubscribed) TrySubscribeDayNight();

        EnsureRefs();
    }

    private void TrySubscribe()
    {
        // 호출부가 이미 막고 있지만(Start 1회 + LateUpdate의 !_subscribed) 형제 코디네이터와 형태를 맞춘다 —
        // 이 셋은 갈라지기 시작하면 조용히 어긋나는 계열이다(WL-145).
        if (_subscribed) return;

        var mm = MouseManager.Instance;
        if (mm == null)
        {
            // TitleScene에서는 MouseManager가 없는 것이 정상이다.
            if (!GameSceneManager.IsTitleScene && !_warnedNoMouseManager)
            {
                _warnedNoMouseManager = true;
                Debug.LogWarning("[주민 드래그] MouseManager가 아직 없어 주민 끌기가 대기 중입니다.");
            }

            return;
        }

        mm.OnUnitDragBegin += HandleUnitDragBegin;
        mm.OnUnitDragEnd += HandleUnitDragEnd;
        _subscribed = true;
    }

    private void TrySubscribeDayNight()
    {
        var dayNight = DayNightManager.Instance;
        if (dayNight == null) return;   // 주민 테스트 씬에는 없을 수 있다 — 경고를 남기지 않는다

        dayNight.OnDayToNight += HandleDayToNight;
        _dayNightSubscribed = true;
    }

    /// `ManagementController`(배치 게이트웨이)와 `ResidentSpawner`(감추기·되돌리기) 둘 다 씬 소유다.
    /// 못 찾았으면 백오프한다 — 이 컴포넌트는 `DontDestroyOnLoad`라 **모든 씬에 상주**하고,
    /// 둘 다 없는 씬(TitleScene·전투 씬)에서는 조기 반환 조건이 영구히 거짓이라 매 프레임 전수 탐색을 돈다.
    private void EnsureRefs()
    {
        if (_management != null && _spawner != null) return;
        if (--_retryCountdown > 0) return;

        _retryCountdown = k_RetryFrames;

        if (_management == null) _management = FindFirstObjectByType<ManagementController>();
        if (_spawner == null) _spawner = FindFirstObjectByType<ResidentSpawner>();
    }

    // 씬이 바뀌면 이전 주민은 이미 파괴됐다. 되돌릴 대상이 없으므로 목록만 비운다(WL-033과 같은 계열 —
    // 죽은 참조를 들고 통지 경로를 타면 그쪽에서 터진다).
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _carried.Clear();
        _visual.Clear();
        _management = null;
        _spawner = null;
        _retryCountdown = 0;

        // 씬이 바뀌면 DayNightManager 인스턴스도 갈린다 — 새 씬에서 다시 붙는다.
        _dayNightSubscribed = false;
    }

    /// 밤이 되면 들고 있던 주민을 **들었던 자리에 그대로 내려놓는다** — §8.2가 정한
    /// "드래그 중 밤 전환 → 행렬 해제 후 전원 R8 귀가"다. 내려놓으면 BT가 되살아나 밤을 읽고
    /// 스스로 문으로 걸어간다(§3.3 — 밤에는 주민이 0명이다).
    ///
    /// **커서 자리가 아니라 들었던 자리로 돌린다.** 밤 전환은 플레이어가 놓은 것이 아니라 시간이 끊은
    /// 것이라, 커서 밑에 떨구면 의도하지 않은 이동이 된다.
    private void HandleDayToNight()
    {
        _visual.AbortAll();
        _carried.Clear();
    }

    /// 착지가 끝났다. **되돌리는 호출은 여기 하나뿐이다** — 연출은 스포너를 직접 부르지 않는다.
    private void HandleLanded(Resident resident, Vector3 landing)
    {
        if (_spawner == null) return;   // 씬이 갈렸다. 되돌릴 주체가 없다.

        _spawner.ReleaseCarried(resident, landing);
    }

    // ── 들기 ──────────────────────────────────────────────────────────

    private void HandleUnitDragBegin(IDragHandle handle)
    {
        // 마커는 도메인 중립이다 — 주민이 아닌 끌기 대상이 생기면 여기서 걸러진다.
        if (handle is not ResidentSelectable pressed) return;

        // 밤에는 주민이 존재하지 않는다(§3.3). 선택이 이미 막혀 있어 여기까지 오기 어렵지만,
        // 조건을 한 곳에만 두면 다른 진입 경로가 생겼을 때 조용히 새어 나간다.
        if (!IsDay) return;

        EnsureRefs();

        if (_spawner == null)
        {
            if (!_warnedNoSpawner)
            {
                _warnedNoSpawner = true;
                Debug.LogWarning("[주민 드래그] ResidentSpawner가 씬에 없어 주민을 들 수 없습니다.");
            }
            return;
        }

        // 앞선 드래그가 종료 통지를 못 받고 끝났을 수 있다(방어). 남아 있으면 먼저 되돌린다.
        ReleaseAll();

        CollectPickTargets(pressed);

        for (int i = 0; i < _pickBuffer.Count; i++)
        {
            Resident resident = _pickBuffer[i] != null ? _pickBuffer[i].Resident : null;

            if (resident == null || !_spawner.TryCarry(resident)) continue;

            _carried.Add(resident);

            // 목록 순서가 곧 탑의 층이다(먼저 잡힌 사람이 아래). 스포너가 자리를 비켜 준 **뒤에** 올린다 —
            // 순서를 뒤집으면 아직 켜져 있는 NavMeshAgent가 첫 프레임에 지면으로 끌어내린다.
            _visual.Lift(resident);
        }
    }

    /// 무엇을 들 것인가. **누른 주민이 선택 집합 안에 있으면 집합 전체를, 아니면 그 1명만** 든다
    /// (RTS·파일 탐색기의 표준 규칙 — 골라 둔 무리를 집으면 무리째, 남을 집으면 그것만).
    ///
    /// 인원 상한(`MaxVillagers − AssignedTotal`, §8)을 여기서 다시 자르지 않는다. 집합은 코디네이터가
    /// 이미 상한까지만 담고 있고, 상한이 줄면 그쪽이 다시 깎는다 — 같은 판정을 두 곳에 두지 않는다.
    private void CollectPickTargets(ResidentSelectable pressed)
    {
        _pickBuffer.Clear();

        ResidentSelectionCoordinator selection = ResidentSelectionCoordinator.Instance;
        IReadOnlyList<ResidentSelectable> selected = selection != null ? selection.Selected : null;

        if (Contains(selected, pressed))
        {
            for (int i = 0; i < selected.Count; i++) _pickBuffer.Add(selected[i]);
            return;
        }

        // 선택 밖의 주민을 집었다 — 상한이 0이면(유휴 주민 없음) 들 수 없다. 집합 경로는 위에서 이미
        // 상한을 통과한 목록이므로 이 검사가 필요 없다.
        if (selection != null && selection.SelectionCap <= 0) return;

        _pickBuffer.Add(pressed);
    }

    private static bool Contains(IReadOnlyList<ResidentSelectable> list, ResidentSelectable target)
    {
        if (list == null) return false;

        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], target)) return true;
        }

        return false;
    }

    // ── 놓기 ──────────────────────────────────────────────────────────

    /// 놓은 지점의 대상을 해석해 배치하거나 되돌린다.
    ///
    /// **성공을 확인한 뒤에 소멸시킨다**(§3.2) — 먼저 지우고 나중에 배치를 시도하면 실패했을 때 주민이
    /// 증발한다. 들었을 때 감춘 것은 소멸이 아니라 보관이고, 여기서 배치가 성사돼야 진짜로 없어진다.
    ///
    /// 실패는 전부 같은 결말로 모인다(바닥 · 생산 건물이 아님 · 밤 · 상한 초과 · 다중 드롭의 남는 인원) —
    /// **놓은 자리 주변에 흩어져 떨어진다.** 거절 피드백(흔들림·토스트)은 §8.3 미정이라 아직 없다.
    private void HandleUnitDragEnd(GameObject dropTarget)
    {
        if (_carried.Count == 0) return;

        // 되돌릴 주체가 사라졌다(씬 전환 등). 들었던 자리로 되돌리는 것조차 스포너를 거쳐야 하므로 목록만 놓는다.
        if (_spawner == null)
        {
            _carried.Clear();
            _visual.Clear();
            return;
        }

        int lineIndex = ResolveProductionLine(dropTarget);

        _dropBuffer.Clear();

        for (int i = 0; i < _carried.Count; i++)
        {
            Resident resident = _carried[i];
            if (resident == null) continue;

            // 밤·인원 상한은 게이트웨이가 판정한다. 여기서 다시 세지 않는 것이 요점이다.
            if (lineIndex >= 0 && _management.AssignVillager(lineIndex))
            {
                _visual.Consume(resident);
                _spawner.ConsumeCarried(resident);
                continue;
            }

            _dropBuffer.Add(resident);
        }

        // 남은 전원을 **한 번에** 터뜨린다 — 착지 지점을 고르게 흩으려면 몇 명인지를 알아야 한다.
        // 놓은 자리는 연출이 커서에서 직접 푼다(경영 공간 지면에는 콜라이더가 없어 물리로 못 짚는다).
        _visual.Burst(_dropBuffer);

        _dropBuffer.Clear();
        _carried.Clear();
    }

    /// 놓은 지점이 어느 생산 라인인가. 생산 건물이 아니면(빈 땅 · 본진 · 상점 · 타워) −1.
    ///
    /// 컨트롤러는 건물을 SO로만 알기 때문에 씬 → SO → 라인으로 두 번 건너간다.
    ///
    /// ⚠ **여기서 좌표를 되짚지 않는다.** 연출 자리는 `MouseManager`가 준 히트 지점을 쓴다 —
    /// 건물 `Obj_*`의 피벗은 콜라이더에서 수백 유닛 떨어져 있고 **세 건물이 같은 좌표를 공유**한다
    /// (배치는 BoxCollider의 `center` 오프셋이 들고 있다). 한때 `info.transform.position`을 썼다가
    /// 소멸 연출이 마을 반대편에서 터졌다.
    private int ResolveProductionLine(GameObject dropTarget)
    {
        if (dropTarget == null || _management == null) return -1;

        // 콜라이더가 건물 루트의 자식일 수 있다 — `BuildingInfo`는 루트에 있다.
        var info = dropTarget.GetComponentInParent<BuildingInfo>();
        if (info == null || info.Asset == null) return -1;

        return _management.LineIndexOf(info.Asset);
    }

    private void ReleaseAll()
    {
        if (_carried.Count == 0) return;

        // 들었던 자리로 돌려보낸다 — 이 경로는 앞선 드래그가 종료 통지를 못 받은 방어용이라
        // "놓은 자리"라고 할 만한 지점이 없다.
        _visual.AbortAll();
        _carried.Clear();
    }

    private static bool IsDay => (
        DayNightManager.Instance == null ||
        DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day
    );
}
