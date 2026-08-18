using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// 주민을 들어서 끌고 다니다 생산 건물에 떨어뜨려 배치하는 경로(Docs/ManagementArea/Resident.md §8).
/// `ResidentSelectionCoordinator`의 형제다 — MouseManager 입력을 받아 도메인으로 번역하는 같은 자리·같은 규약.
///
/// ── 지금 들어간 범위 ────────────────────────────────────────────────
///
/// **연출은 아직 아무것도 정해지지 않았다**(§8.1 부양 높이 H · §8.3 흩뿌리기 · §8.5 보따리 여부). 그래서
/// 이 단계는 **손맛이 아니라 배선만** 세운다:
///
/// | 조작 | 지금 일어나는 일 |
/// |---|---|
/// | 주민을 눌러 끌기 시작 | 대상이 **그 자리에서 사라진다**(스포너가 감춘다). 커서를 따라오지 않는다 |
/// | 생산 건물에 놓기 | `AssignVillager`로 배치. 성공한 인원은 그대로 소멸 |
/// | 바닥·그 밖에 놓기 | **들었던 자리에 그대로 되돌아온다**(사실상 취소) |
///
/// 공중 행렬(R10) · 어지러움(R11) · 착지 스냅 · 거절 피드백은 전부 이 위에 올라간다. 배치 판정과 인원 회계는
/// 그때도 바뀌지 않으므로, 먼저 세워 두면 연출을 붙일 때 그쪽만 보면 된다.
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
            Destroy(gameObject);
            return;
        }
        s_instance = this;
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
            if (DayNightManager.Instance != null && !_warnedNoMouseManager)
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
        _management = null;
        _spawner = null;
        _retryCountdown = 0;

        // 씬이 바뀌면 DayNightManager 인스턴스도 갈린다 — 새 씬에서 다시 붙는다.
        _dayNightSubscribed = false;
    }

    /// 밤이 되면 들고 있던 주민을 **놓지 않고 목록만 비운다.** 스포너도 같은 이벤트로 임자를 떼고, 주민은
    /// 비활성 그대로 남아 그것이 곧 귀가가 된다(§3.3 — 밤에는 주민이 0명이다).
    ///
    /// §8.2는 "드래그 중 밤 전환 → 행렬 해제 후 전원 R8 귀가"로 정해 뒀다. 지금은 들린 주민이 이미 화면에
    /// 없으므로 걸어서 귀가할 몸이 없고, 결과(사라진다)는 같다. 공중 행렬이 들어오면 그때 문까지 걸리면 된다.
    private void HandleDayToNight() => _carried.Clear();

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

            if (resident != null && _spawner.TryCarry(resident)) _carried.Add(resident);
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
    /// **들었던 자리로 되돌아온다.** 거절 피드백(흔들림·토스트)은 §8.3 미정이라 아직 없다.
    private void HandleUnitDragEnd(GameObject dropTarget)
    {
        if (_carried.Count == 0) return;

        if (_spawner == null)
        {
            _carried.Clear();   // 되돌릴 주체가 사라졌다(씬 전환 등). 목록만 놓는다.
            return;
        }

        int lineIndex = ResolveProductionLine(dropTarget);

        for (int i = 0; i < _carried.Count; i++)
        {
            Resident resident = _carried[i];
            if (resident == null) continue;

            // 밤·인원 상한은 게이트웨이가 판정한다. 여기서 다시 세지 않는 것이 요점이다.
            if (lineIndex >= 0 && _management.AssignVillager(lineIndex))
            {
                _spawner.ConsumeCarried(resident);
                continue;
            }

            _spawner.ReleaseCarried(resident);
        }

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

        for (int i = 0; i < _carried.Count; i++)
        {
            if (_carried[i] != null) _spawner.ReleaseCarried(_carried[i]);
        }

        _carried.Clear();
    }

    private static bool IsDay => (
        DayNightManager.Instance == null ||
        DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day
    );
}
