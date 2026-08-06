using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// 주민 선택의 두뇌. `TowerMergeCoordinator`와 같은 자리·같은 규약이다 — MouseManager 입력을 받아
/// 선택 집합을 소유하고, 낮 게이팅·인원 상한·아웃라인 diff를 담당한다.
/// MouseManager는 마커(`ResidentSelectable`) 유무만 알고 "주민"을 모른다.
///
/// ── 인원 상한 ────────────────────────────────────────────────────────
/// 드래그로 5명을 걸쳐도 **지금 부릴 수 있는 인원까지만** 선택된다. 기준은 유휴 주민 수
/// (`MaxVillagers - AssignedTotal`)로, 본진 팝업이 보여주는 유휴 인원과 `HasIdleVillagers`의
/// 판정 기준과 같은 출처다 — 화면이 "1명 남았다"고 말하는데 2명이 잡히면 안 된다.
///
/// 넘칠 때 **뒤를 버린다**. `MouseManager._boxHits`가 사각형에 들어온 순서를 유지하므로
/// 앞을 남기는 것이 곧 "먼저 잡힌 순서대로"가 된다. 상한이 런타임에 줄어드는 경우
/// (선택 상태로 생산 라인에 배정)도 같은 규칙으로 뒤에서 깎는다.
///
/// ── 씬을 건드리지 않는다 ──────────────────────────────────────────────
/// `OutlineInteractionDriver`와 같은 이유로 런타임에 스스로 부팅한다(정본 씬 병합 규칙
/// `Docs/Core/SceneWorkflow.md`와의 충돌 방지). 그래서 튜닝값이 인스펙터가 아니라 코드에 있고,
/// `ManagementController`도 인스펙터 배선 없이 런타임에 찾는다.
[DisallowMultipleComponent]
public class ResidentSelectionCoordinator : MonoBehaviour
{
    /// 레지스트리 멤버십이 개수 변화 없이 바뀌는 경우(같은 프레임에 하나 죽고 하나 생김)를 위한
    /// 안전 재스캔 주기. 개수 비교만으로는 놓치는 창이 있는데, 매 프레임 전수 검사는 낭비다.
    private const int k_RescanIntervalFrames = 30;

    private static ResidentSelectionCoordinator s_instance;
    public static ResidentSelectionCoordinator Instance => s_instance;

    // 선택 집합. **순서가 의미를 가진다**(상한 초과 시 앞을 남긴다) → HashSet이 아니라 List.
    private readonly List<ResidentSelectable> _selected = new();

    // 현재 아웃라인이 켜진 대상(진입 경로 무관 diff용).
    private readonly HashSet<ResidentSelectable> _highlighted = new();

    // ── 드래그 사각형 선택 ────────────────────────────────────────────
    private bool _boxDragging;
    private readonly List<ResidentSelectable> _dragBase = new();   // 드래그 시작 시점의 기준 집합
    private readonly List<ResidentSelectable> _dragResult = new(); // 기준 + 사각형 내용, 매 갱신 재구성

    // SetAll 전용 버퍼. `_dragResult`를 재사용하면 안 된다 — 드래그 경로가 `_dragResult`를 **인자로 넘기므로**
    // 같은 리스트를 쓰면 SetAll이 읽는 중에 자기 입력을 비운다.
    private readonly List<ResidentSelectable> _applyBuffer = new();

    // 단일 클릭용 1개짜리 스크래치. 매 클릭 배열을 새로 만들지 않기 위한 것.
    private readonly List<ResidentSelectable> _singleBuffer = new();

    private ManagementController _management;
    private bool _subscribed;
    private bool _warnedNoMouseManager;
    private bool _warnedNoManagement;
    private bool _warnedNoCollider;
    private bool _warnedNoLayer;

    private int _lastResidentCount = -1;
    private int _rescanCountdown;
    private int _selectableLayer = -2; // -2=미조회, -1=레이어 없음

    // 마지막으로 주민에게 적용한 레이어. 상한이 0이 되면 Selectable에서 내려 **레이캐스트 자체를 끊는다**.
    // 이게 없으면 클릭 단일 선택이 상한을 우회한다 — MouseManager.Select가 ISelectable을 찾는 순간
    // OutlineInteractionDriver가 초록을 켜 버리고, 그 경로는 이 코디네이터를 거치지 않기 때문이다.
    // (드래그는 레지스트리 순회라 SetAll의 상한에 막히지만, 클릭은 레이캐스트라 안 막힌다.)
    private int _appliedLayer = -99;

    // MouseManager가 현재 단일 선택 중인 주민(있다면). MouseManager는 _selected를 공개하지 않으므로
    // 여기서 따라 들고 있다가, 상한이 0이 될 때 그 대상만 골라 푼다.
    private ResidentSelectable _lastSingle;

    /// 선택된 주민(선택 순서). 패널·배정 UI가 붙을 때 이 파사드를 본다.
    public IReadOnlyList<ResidentSelectable> Selected => _selected;

    /// 선택 집합이 바뀔 때 발행.
    public event Action OnSelectionChanged;

    /// 지금 선택할 수 있는 최대 인원(유휴 주민 수). `ManagementController`가 없으면 제한하지 않는다.
    public int SelectionCap => ComputeCap();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;

        var go = new GameObject(nameof(ResidentSelectionCoordinator));
        s_instance = go.AddComponent<ResidentSelectionCoordinator>();
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
        var mm = MouseManager.Instance;
        if (mm != null && _subscribed)
        {
            mm.OnSelectionChanged -= HandleSelectionChanged;
            mm.OnGroupSelectToggled -= HandleGroupToggle;
            mm.OnBoxSelectBegin -= HandleBoxSelectBegin;
            mm.OnBoxSelectUpdate -= HandleBoxSelectUpdate;
            mm.OnBoxSelectEnd -= HandleBoxSelectEnd;
        }

        if (_management != null) _management.OnChanged -= HandleManagementChanged;

        if (s_instance == this) s_instance = null;
    }

    private void LateUpdate()
    {
        // MouseManager가 뒤늦게(다른 씬에서) 등장할 수 있어 붙을 때까지 확인한다.
        if (!_subscribed) TrySubscribe();

        EnsureManagement();
        EnsureMarkers();
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;

        var mm = MouseManager.Instance;
        if (mm == null)
        {
            if (!_warnedNoMouseManager)
            {
                _warnedNoMouseManager = true;
                Debug.LogWarning("[주민 선택] MouseManager가 아직 없어 주민 선택이 대기 중입니다.");
            }
            return;
        }

        mm.OnSelectionChanged += HandleSelectionChanged;
        mm.OnGroupSelectToggled += HandleGroupToggle;
        mm.OnBoxSelectBegin += HandleBoxSelectBegin;
        mm.OnBoxSelectUpdate += HandleBoxSelectUpdate;
        mm.OnBoxSelectEnd += HandleBoxSelectEnd;
        _subscribed = true;
    }

    // 씬이 바뀌면 이전 대상은 이미 파괴됐다. MouseManager도 같은 시점에 알림 없이 필드만 리셋하므로
    // (WL-033) 해제 이벤트가 오지 않는다 → 이쪽 상태도 직접 비운다.
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _selected.Clear();
        _highlighted.Clear();
        _dragBase.Clear();
        _dragResult.Clear();
        _boxDragging = false;
        _lastResidentCount = -1;
        _appliedLayer = -99;
        _lastSingle = null;

        if (_management != null) _management.OnChanged -= HandleManagementChanged;
        _management = null;
    }

    // ── 마커 부착 ─────────────────────────────────────────────────────
    // 주민은 스포너가 런타임에 만들기도 하고 씬에 직접 놓기도 한다. 어느 경로든 `Resident`는
    // `ResidentRegistry`에 스스로 등록하므로, 그 목록을 유일한 출처로 삼아 마커와 레이어를 맞춘다.
    private void EnsureMarkers()
    {
        var residents = ResidentRegistry.Residents;

        if (_selectableLayer == -2)
        {
            _selectableLayer = LayerMask.NameToLayer("Selectable");
            if (_selectableLayer < 0 && !_warnedNoLayer)
            {
                _warnedNoLayer = true;
                Debug.LogWarning("[주민 선택] 'Selectable' 레이어가 없어 주민 클릭·호버가 동작하지 않습니다.");
            }
        }

        // 상한이 0이면 Default로 내려 클릭·호버 자체가 안 걸리게 한다(드래그와 동작을 맞춘다).
        int wantLayer = (_selectableLayer >= 0 && ComputeCap() > 0) ? _selectableLayer : 0;

        if (--_rescanCountdown > 0 && residents.Count == _lastResidentCount && wantLayer == _appliedLayer) return;

        _rescanCountdown = k_RescanIntervalFrames;
        _lastResidentCount = residents.Count;
        _appliedLayer = wantLayer;

        for (int i = 0; i < residents.Count; i++)
        {
            Resident r = residents[i];
            if (r == null) continue;

            GameObject go = r.gameObject;

            if (!go.TryGetComponent(out ResidentSelectable _))
            {
                go.AddComponent<ResidentSelectable>();
            }

            // MouseManager의 `_selectableMask`가 Selectable 레이어만 보므로, 레이어가 맞지 않으면
            // 콜라이더가 있어도 레이캐스트에 걸리지 않는다 — 증상은 "아웃라인이 안 뜬다"뿐이다.
            if (go.layer != wantLayer)
            {
                go.layer = wantLayer;
            }

            if (!_warnedNoCollider && go.GetComponent<Collider>() == null)
            {
                _warnedNoCollider = true;
                Debug.LogWarning($"[주민 선택] {go.name}에 Collider가 없어 클릭·호버가 걸리지 않습니다. " +
                                 "주민 프리팹에 CapsuleCollider가 필요합니다.", go);
            }
        }
    }

    private void EnsureManagement()
    {
        if (_management != null) return;

        _management = FindFirstObjectByType<ManagementController>();
        if (_management == null)
        {
            if (!_warnedNoManagement)
            {
                _warnedNoManagement = true;
                Debug.LogWarning("[주민 선택] ManagementController가 없어 선택 인원 상한을 적용하지 않습니다.");
            }
            return;
        }

        // 배정 수·주민 상한이 바뀌면 이미 선택한 집합이 상한을 넘을 수 있다 → 그때 다시 깎는다.
        _management.OnChanged += HandleManagementChanged;
    }

    /// 유휴 주민 수. 상한의 유일한 출처다.
    private int ComputeCap()
    {
        if (_management == null) return int.MaxValue; // 주민 테스트 씬 — 제한하지 않는다
        return Mathf.Max(0, _management.MaxVillagers - _management.AssignedTotal);
    }

    // 배정이 바뀌어 상한이 줄면 집합을 다시 깎는다. 상한이 0이 되는 순간에는 **MouseManager의 단일 선택도
    // 풀어야** 한다 — 그 초록은 OutlineInteractionDriver가 들고 있어서 이 코디네이터가 집합을 비워도 안 꺼진다.
    // 남의 선택(타워 등)까지 건드리지 않도록 마지막 단일 선택이 주민이었을 때만 푼다.
    private void HandleManagementChanged()
    {
        SetAll(_selected);

        if (ComputeCap() > 0 || _lastSingle == null) return;

        _lastSingle = null;
        MouseManager.Instance?.ClearSelection();
    }

    // ── MouseManager 입력 ─────────────────────────────────────────────

    // 단일 클릭. 주민이 아니면(타워·건물·빈 곳) 집합을 비운다 — 타워 코디네이터가 주민 클릭에
    // 자기 집합을 비우는 것과 대칭이다.
    private void HandleSelectionChanged(ISelectable sel)
    {
        _lastSingle = sel as ResidentSelectable;   // 상한이 0으로 떨어질 때 풀어 줄 대상

        if (!IsDay) { Clear(); return; }

        if (sel is ResidentSelectable rs)
        {
            _singleBuffer.Clear();
            _singleBuffer.Add(rs);
            SetAll(_singleBuffer);
        }
        else Clear();
    }

    // Shift 추가 선택(토글). 상한에 걸리면 **추가만** 무시한다 — 이미 선택된 것을 빼지는 않는다.
    private void HandleGroupToggle(IGroupSelectable g)
    {
        if (!IsDay) return;
        if (g is not ResidentSelectable rs) return;

        if (_selected.Contains(rs))
        {
            _selected.Remove(rs);
            Commit();
            return;
        }

        if (_selected.Count >= ComputeCap()) return;

        _selected.Add(rs);
        Commit();
    }

    private void HandleBoxSelectBegin(bool additive)
    {
        if (!IsDay) return;

        _boxDragging = true;
        _dragBase.Clear();
        if (additive) _dragBase.AddRange(_selected);
    }

    // 기준 + 사각형 내용(들어온 순서)으로 집합을 원자 교체하고, 상한까지만 남긴다.
    private void HandleBoxSelectUpdate(IReadOnlyList<IGroupSelectable> hits)
    {
        if (!IsDay || !_boxDragging) return;

        _dragResult.Clear();

        // 기준 집합은 드래그 시작 시점의 사본이라 그새 사라진(밤 귀가·파괴) 주민이 섞일 수 있다.
        for (int i = 0; i < _dragBase.Count; i++)
        {
            ResidentSelectable rs = _dragBase[i];
            if (!IsAlive(rs) || _dragResult.Contains(rs)) continue;
            _dragResult.Add(rs);
        }

        for (int i = 0; i < hits.Count; i++)
        {
            // 마커는 도메인 중립이라 주민 해석은 여기서 한다(Shift 토글 경로와 같은 규칙).
            // 타워 마커가 섞여 들어오므로 반드시 타입으로 걸러야 한다.
            if (hits[i] is not ResidentSelectable rs) continue;
            if (!IsAlive(rs) || _dragResult.Contains(rs)) continue;
            _dragResult.Add(rs);
        }

        SetAll(_dragResult);
    }

    private void HandleBoxSelectEnd()
    {
        if (!_boxDragging) return;
        _boxDragging = false;
    }

    // ── 집합 조작 ─────────────────────────────────────────────────────

    /// 집합을 원자 교체한다. 죽은 항목을 걸러내고 상한까지 앞에서 채운다.
    /// `source`가 `_selected` 자신일 수 있으므로(재클램프 경로) 버퍼를 거쳐 쓴다.
    private void SetAll(IReadOnlyList<ResidentSelectable> source)
    {
        int cap = ComputeCap();

        _applyBuffer.Clear();
        for (int i = 0; i < source.Count; i++)
        {
            ResidentSelectable rs = source[i];
            if (!IsAlive(rs) || _applyBuffer.Contains(rs)) continue;
            if (_applyBuffer.Count >= cap) break; // 넘치면 뒤를 버린다 = 먼저 잡힌 순서대로 남는다
            _applyBuffer.Add(rs);
        }

        if (SameAs(_applyBuffer)) return; // 안 바뀐 프레임은 통지·diff 없이 흡수한다

        _selected.Clear();
        _selected.AddRange(_applyBuffer);
        Commit();
    }

    private void Clear()
    {
        if (_selected.Count == 0) return;
        _selected.Clear();
        Commit();
    }

    private bool SameAs(List<ResidentSelectable> other)
    {
        if (_selected.Count != other.Count) return false;
        for (int i = 0; i < _selected.Count; i++)
        {
            if (!ReferenceEquals(_selected[i], other[i])) return false;
        }
        return true;
    }

    private void Commit()
    {
        RefreshHighlight();
        OnSelectionChanged?.Invoke();
    }

    // 새로 들어온 주민은 아웃라인 on, 빠졌거나 죽은 주민은 off + 추적 해제(대칭 보장).
    private void RefreshHighlight()
    {
        for (int i = 0; i < _selected.Count; i++)
        {
            ResidentSelectable rs = _selected[i];
            if (rs == null) continue;

            if (_highlighted.Add(rs)) rs.OnGroupSelected();
        }

        _highlighted.RemoveWhere(rs =>
        {
            if (rs != null && _selected.Contains(rs)) return false; // 유지
            if (rs != null) rs.OnGroupDeselected();
            return true;
        });
    }

    /// 파괴됐거나 비활성(밤에 스포너가 거둔 주민)인지. 선택 집합에 남겨 두면 유령 아웃라인이 된다.
    private static bool IsAlive(ResidentSelectable rs) => rs != null && rs.isActiveAndEnabled;

    private static bool IsDay => (
        DayNightManager.Instance == null ||
        DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day
    );
}
