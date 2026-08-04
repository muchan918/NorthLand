using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;   // 프로젝트는 신규 Input System 사용
using UnityEngine.SceneManagement;
using CombatSpace;               // CombatMapTileView / CombatTileType (전투 타일 판정)

/// 클릭으로 선택 가능한 배치물(타워·건물 등)이 구현한다. (요구사항 ②)
public class MouseManager : MonoBehaviour
{
    enum Mode
    {
        Idle,
        BoxSelect,      // Idle의 하위 상태 — 배치·조준 중에는 진입할 수 없다(#261)
        Placement,
        SkillTargeting
    }

    public static MouseManager Instance { get; private set; }

    public event Action<ISelectable> OnSelectionChanged;
    // Shift(추가 선택 키) + IGroupSelectable 대상 클릭 시 발행(토글). 그룹 선택 집합은 TowerMergeCoordinator가
    // 소유하고, MouseManager는 마커 유무만 알 뿐 대상 타입(타워)은 모른다(입력 단일 창구·제네릭 유지).
    // 이 경로에서는 단일 _selected를 건드리지 않는다.
    public event Action<IGroupSelectable> OnGroupSelectToggled;
    // 평클릭(추가키 없음)·Esc·빈 곳 클릭 시 해석된 대상(ISelectable 또는 null)을 **중복 제거 없이 항상** 발행한다.
    // OnSelectionChanged는 _selected 변화만(deduped) 통지하므로, Shift로만 선택한 상태(_selected==null)에서
    // Esc·빈 곳 클릭의 해제 신호가 `Select(null)`의 조기 반환에 삼켜지는 문제가 있었다(WL-085). 그룹 선택
    // 코디네이터는 이 이벤트로 집합을 리셋(타워면 단일화)/해제한다. 단일 선택(_selected) 상태는 안 바꾼다.
    public event Action<ISelectable> OnPrimarySelect;
    // 커서 밑 호버 대상이 바뀔 때만 통지(없으면 null). 툴팁 UI가 구독해 표시/숨김을 결정한다.
    public event Action<IHoverable> OnHoverChanged;

    // ── 드래그 사각형 선택(#261) 3단계 통지 ──────────────────────────
    // MouseManager는 사각형에 무엇이 걸렸는지만 알리고 집합은 들지 않는다. 순서 있는 집합의 소유·해석은
    // 도메인 코디네이터(타워=TowerMergeCoordinator)가 계속 담당한다.
    /// 드래그 시작. 인자는 추가 선택(Shift) 여부 — 구독자가 기준 집합을 스냅샷할 시점이다.
    public event Action<bool> OnBoxSelectBegin;
    /// 사각형 내용이 바뀔 때마다 발행. **사각형에 들어온 순서**가 그대로 보존된 목록이다.
    public event Action<IReadOnlyList<IGroupSelectable>> OnBoxSelectUpdate;
    /// 드래그 종료(버튼 뗌 또는 Esc). 구독자가 유예했던 갱신을 여기서 한 번 처리한다.
    public event Action OnBoxSelectEnd;

    /// 드래그 중인 사각형의 스크린 좌표 영역. 사각형 UI가 매 프레임 읽어 간다(IsBoxSelecting이 true일 때만 유효).
    public Rect BoxSelectScreenRect { get; private set; }
    public bool IsBoxSelecting => _mode == Mode.BoxSelect;
    /// 배치 고스트를 들고 있는가. 되돌리기 버튼(#281)이 "먼저 이 고스트부터 치운다"를 판정하는 데 쓴다.
    public bool IsPlacing => _mode == Mode.Placement;
    // 현재 포인터 화면 좌표. 다른 시스템(툴팁 등)이 Mouse.current를 직접 읽지 않고 여기서 얻는다(입력 단일 창구 계약).
    public Vector2 PointerPosition { get; private set; }

    private Mode _mode = Mode.Idle;

    [SerializeField] Camera _camera;

    [Header("Raycast Layers")]
    // 선택 후보 레이어(타워/건물...). 최종 선택 여부는 ISelectable 유무로 판정하므로, 레이어는 굵은 필터일 뿐.
    [SerializeField] LayerMask _selectableMask;
    // 배치 표면 레이어(바닥/그리드). 고스트가 이 위에 올라간다.
    [SerializeField] LayerMask _placementMask;

    private ISelectable _selected;
    private IHoverable _hovered;
    private PlacementRequest _request;
    private SkillTargetRequest _skillRequest;
    private GameObject _ghost;

    // ── 좌클릭 제스처 상태 ──────────────────────────────────────────
    // 누른 순간에는 클릭인지 드래그인지 알 수 없으므로(#261), 선택 확정을 **뗄 때**로 미루고
    // 누를 때는 시작점만 기록한다. UI 위에서 시작한 제스처는 아예 채택하지 않는다(_pressActive=false).
    private bool _pressActive;
    private Vector2 _pressScreenPos;
    private bool _pressAdditive; // 추가 선택 키(Shift)는 **누른 시점** 상태로 고정 — 도중에 눌렀다 떼도 안 바뀐다

    // ── 드래그 사각형 선택 상태(#261) ───────────────────────────────
    [Header("Box Select")]
    [Tooltip("이 픽셀 거리를 넘게 움직이면 클릭이 아니라 드래그로 본다")]
    [SerializeField] float _dragThreshold = 8f;

    private Vector2 _boxAnchor;   // 누른 지점(스크린) — 사각형의 고정 모서리
    private bool _boxAdditive;

    // 사각형에 걸린 대상을 **들어온 순서대로** 쌓는다. 빠지면 지우고, 다시 들어오면 맨 뒤로 붙는다.
    // 재판정에 투영 기준점이 필요해 Entry(마커+Transform)째로 들고, 통지용 목록은 변경 시에만 다시 만든다.
    private readonly List<GroupSelectableRegistry.Entry> _boxHits = new();
    private readonly List<IGroupSelectable> _boxTargets = new();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SetCamera(Camera.main); // 최초 부트 씬은 sceneLoaded가 이미 지나간 뒤라 한 번 직접 호출 필요
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SetCamera(Camera.main);

        // 이전 씬의 선택/호버 대상은 이미 파괴됐을 수 있다. ISelectable/IHoverable은 인터페이스 타입이라
        // Unity의 파괴 감지(오버로드된 ==)가 이 타입으로는 걸리지 않으므로, 알림 호출(OnDeselected 등) 없이
        // 필드만 직접 리셋한다(WL-033) — _selected?.OnDeselected()를 거치면 죽은 참조를 그대로 호출해 터진다.
        _selected = null;
        _hovered = null;
        ResetGesture();
        CancelPlacement();
        CancelSkillTargeting();
    }

    private void Update()
    {
        var screenPos = Mouse.current.position.ReadValue();
        PointerPosition = screenPos;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        switch (_mode)
        {
            case Mode.Idle: UpdateIdle(screenPos, overUI); break;
            case Mode.BoxSelect: UpdateBoxSelect(screenPos); break;
            case Mode.Placement: UpdatePlacement(screenPos, overUI); break;
            case Mode.SkillTargeting: UpdateSkillTargeting(screenPos, overUI); break;
        }
    }

    public void SetCamera(Camera cam)
    {
        if (cam == null)
        {
            Debug.LogWarning("[MouseManager] MainCamera 태그가 붙은 카메라를 찾지 못했습니다.");
        }
        _camera = cam;
    }

    /// **모드 전환의 유일한 창구.** `_mode`에 직접 대입하지 말 것.
    ///
    /// BoxSelect를 벗어날 때 종료 통지(`OnBoxSelectEnd`)를 반드시 1회 발행하기 위한 이음매다. 예전에는
    /// `ResetGesture`가 그 역할을 했는데, `CancelPlacement`/`CancelSkillTargeting`이 `_mode = Idle`을 **먼저**
    /// 대입해 버려서 뒤따르는 `ResetGesture`의 `_mode == BoxSelect` 검사가 항상 거짓이었다(WL-143).
    /// 그러면 구독자의 유예 플래그가 켜진 채 남아 우측 패널이 영구 정지한다. 전환 지점을 여기 하나로 모으면
    /// 호출 순서와 무관하게 통지가 보장된다 — `PhasePanelSwitcher`처럼 Cancel을 직접 부르는 경로도 포함.
    private void SetMode(Mode next)
    {
        if (_mode == Mode.BoxSelect && next != Mode.BoxSelect) ExitBoxSelect();
        _mode = next;
    }

    /// <summary>
    /// 진행 중인 포인터 상호작용과 선택 상태를 정본 순서로 모두 취소한다.
    /// </summary>
    public void CancelInteractions()
    {
        CancelPlacement();
        CancelSkillTargeting();
        ClearHover();
        ClearSelection();
    }

    // ── 외부 진입점 ────────────────────────────────────────────────
    public void BeginPlacement(PlacementRequest request)
    {
        CancelInteractions();
        ResetGesture();   // 버튼을 누른 채 배치가 시작됐다면 그 제스처는 버린다
        _request = request;
        _ghost = Instantiate(request.GhostPrefab);
        SetMode(Mode.Placement);
    }

    public void CancelPlacement()
    {
        _request?.OnEnded?.Invoke(); // 배치 종료(취소/확정 복귀) 시 요청이 만든 프리뷰 등을 정리할 수 있게 통지
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _request = null;
        SetMode(Mode.Idle);
    }

    // 스킬 타겟팅(#103): 그리드 스냅·점유 검증이 필요 없는 PlacementRequest의 경량 버전.
    // 클릭한 위치를 그대로 SkillTargetRequest.OnConfirmed로 넘긴다(요구사항: SystemMap §4 계약).
    public void BeginSkillTargeting(SkillTargetRequest request)
    {
        CancelPlacement();
        CancelSkillTargeting();
        ResetGesture();
        ClearHover(); // 타겟팅 중에는 툴팁을 띄우지 않는다
        _skillRequest = request;
        _ghost = Instantiate(request.GhostPrefab);
        SetMode(Mode.SkillTargeting);
    }

    public void CancelSkillTargeting()
    {
        _skillRequest?.OnEnded?.Invoke();
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _skillRequest = null;
        SetMode(Mode.Idle);
    }

    // ── Idle: 선택 (요구사항 ②) ────────────────────────────────────
    private void UpdateIdle(Vector2 screenPos, bool overUI)
    {
        UpdateHover(screenPos, overUI);

        // Esc → 전체 해제(그룹 포함). 코디네이터가 OnSelectionChanged(null)을 받아 집합을 비운다.
        // (우클릭은 카메라 드래그·조준 취소와 이미 이중 점유라 해제에 쓰지 않는다 — WL-073)
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ResetGesture(); // Esc = 진행 중 제스처 폐기. 안 버리면 버튼을 뗄 때 클릭이 한 번 더 확정된다(WL-144)
            ClearSelection();
            return;
        }

        var left = Mouse.current.leftButton;

        // 누를 때: 시작점만 기록하고 확정은 미룬다. 이 시점엔 클릭인지 드래그인지 알 수 없다(#261).
        if (left.wasPressedThisFrame)
        {
            _pressActive = !overUI; // UI 위에서 시작한 제스처는 채택하지 않는다
            _pressScreenPos = screenPos;
            _pressAdditive = IsAdditivePressed();

            // 같은 프레임에 뗌까지 함께 보고되는 경우(저프레임·히칭)를 여기서 소화한다. 그냥 return하면
            // 다음 프레임엔 wasReleasedThisFrame이 false·isPressed도 false라 아래 방어가 제스처를 버려
            // **클릭이 통째로 유실된다**(WL-144). 이동 거리가 0이므로 판정은 자동으로 클릭이다.
            if (!left.wasReleasedThisFrame) return;
        }

        if (!_pressActive) return;

        // 뗄 때 확정 = 클릭.
        if (left.wasReleasedThisFrame)
        {
            _pressActive = false;
            if (!overUI) CommitClick(screenPos, _pressAdditive);
            return;
        }

        // 방어: 다른 모드를 거쳐 돌아오는 등으로 뗀 프레임을 놓쳤으면 제스처를 버린다.
        if (!left.isPressed)
        {
            _pressActive = false;
            return;
        }

        // 임계 거리를 넘으면 드래그로 승격(#261).
        if ((screenPos - _pressScreenPos).sqrMagnitude >= _dragThreshold * _dragThreshold)
        {
            BeginBoxSelect(screenPos);
        }
    }

    /// 임계 미만으로 움직인 좌클릭의 확정 처리 — 기존 단일 선택 / Shift 토글 규칙 그대로다.
    /// 확정 시점만 누를 때 → 뗄 때로 옮겼고, 판정 내용은 바뀌지 않았다.
    private void CommitClick(Vector2 screenPos, bool additive)
    {
        bool hitSelectable = RaycastMask(screenPos, _selectableMask, out var hit);

        if (additive)
        {
            // Shift 추가 선택: 그룹 선택 가능(IGroupSelectable 마커) 대상만 토글 통지.
            // 건물·영지 노드·빈 곳 등 마커 없는 대상은 무시(집합 불변 — _selected도 유지).
            if (hitSelectable && hit.collider.TryGetComponent(out IGroupSelectable grp))
            {
                // 토글이 실제로 일어나는 경우에만, 그룹 경로로 넘어가기 전에 단일 선택을 먼저 비운다.
                // 단일 선택의 부수 표시(사거리 원 + 인포 패널)는 대상의 OnDeselected로만 꺼지는데, 이 경로에서
                // _selected를 그대로 두면 아무도 그걸 부르지 않아 **합성 패널 위에 직전 타워의 사거리 원이 잔존**한다
                // (코디네이터 RefreshPanel은 TowerInfoUI만 내릴 수 있고 남의 사거리 원은 모른다 — WL-087 계열).
                // 이후 표시는 집합 크기가 결정한다: 1개면 코디네이터가 그 타워의 OnSelected를 재호출해 복구,
                // 2개 이상이면 합성 패널만. 초록 아웃라인은 GroupSelected 플래그가 이어받으므로 끊기지 않는다.
                // 순서 주의: 토글 뒤에 비우면 count==1로 복귀할 때 코디네이터가 켠 인포·원을 도로 끈다.
                Select(null);
                OnGroupSelectToggled?.Invoke(grp);
            }
            return;
        }

        // 평클릭: 기존 단일 선택(중복 제거 유지 — 기존 구독자용) + 그룹용 OnPrimarySelect를 항상 발행한다.
        // OnPrimarySelect는 _selected 중복 제거와 무관하게 발행되므로, "이미 _selected인 타워 재클릭=단일화"·
        // "빈 곳 클릭=해제"가 항상 코디네이터에 전달된다(WL-085).
        ISelectable picked = null;
        if (hitSelectable) hit.collider.TryGetComponent(out picked);
        Select(picked);
        OnPrimarySelect?.Invoke(picked);
    }

    // 추가 선택 키(Shift) 판정은 입력 단일 창구인 MouseManager가 소유한다(계약 #1).
    private static bool IsAdditivePressed()
    {
        return Keyboard.current != null &&
               (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    }

    /// 진행 중인 좌클릭 제스처를 버린다. 배치·조준으로 모드가 바뀌거나 씬이 갈릴 때, 누른 상태만 남아
    /// 돌아온 뒤 엉뚱한 클릭으로 확정되는 것을 막는다.
    /// (드래그 중이었다면 모드 이탈은 `SetMode`가 책임진다 — 여기서 중복 처리하지 않는다)
    private void ResetGesture() => _pressActive = false;

    // ── BoxSelect: 드래그 사각형 선택 (#261) ───────────────────────
    private void BeginBoxSelect(Vector2 screenPos)
    {
        SetMode(Mode.BoxSelect);
        _pressActive = false;
        _boxAnchor = _pressScreenPos;   // 앵커는 커서가 아니라 **누른 지점**이다
        _boxAdditive = _pressAdditive;
        _boxHits.Clear();
        _boxTargets.Clear();
        BoxSelectScreenRect = MakeRect(_boxAnchor, screenPos);

        ClearHover(); // 드래그 중에는 툴팁·호버 하이라이트를 띄우지 않는다(배치 모드와 같은 규칙)

        // 단일 선택의 부수 표시(사거리 원 + 인포 패널)는 대상의 OnDeselected로만 꺼진다. 그룹 경로로
        // 넘어가기 전에 비우지 않으면 아무도 그걸 부르지 않아 잔존한다 — Shift 클릭 경로와 같은 이유(WL-087 계열).
        Select(null);

        OnBoxSelectBegin?.Invoke(_boxAdditive);
        // 빈 목록으로 1회 발행 — 비추가 드래그면 이 시점에 기존 집합이 즉시 비워진다.
        OnBoxSelectUpdate?.Invoke(_boxTargets);
    }

    private void UpdateBoxSelect(Vector2 screenPos)
    {
        // Esc → 드래그 중단 + 전체 해제. 드래그 이전 상태로 되돌리는 취소는 제공하지 않는다(명세 §5.1).
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            EndBoxSelect();
            ClearSelection();
            return;
        }

        // 커서가 UI 위를 지나가도 드래그는 계속된다 — 채택 여부는 누른 시점에 이미 결정됐다.
        BoxSelectScreenRect = MakeRect(_boxAnchor, screenPos);

        if (RefreshBoxHits()) OnBoxSelectUpdate?.Invoke(_boxTargets);

        // wasReleasedThisFrame이 아니라 isPressed로 판정한다. 포커스 상실·디바이스 리셋 등으로 뗀 프레임을
        // 놓치면 이 모드에 **영구 고착**되고, 그러면 UpdateIdle이 아예 돌지 않아 모든 클릭·호버가 죽는다.
        // 상태를 보고 나가면 뗀 순간을 놓쳐도 다음 프레임에 반드시 빠져나온다(WL-143).
        if (!Mouse.current.leftButton.isPressed) EndBoxSelect();
    }

    private void EndBoxSelect() => SetMode(Mode.Idle); // 정리·통지는 SetMode → ExitBoxSelect가 맡는다

    /// BoxSelect를 벗어날 때의 정리·통지. **`SetMode`만 호출한다**(직접 부르면 모드가 어긋난다).
    private void ExitBoxSelect()
    {
        BoxSelectScreenRect = default;
        _boxHits.Clear();
        _boxTargets.Clear();
        OnBoxSelectEnd?.Invoke();
    }

    /// 사각형 내용을 갱신한다. 바뀌었으면 true.
    /// 빠진 것을 먼저 지우고 새로 들어온 것을 **끝에 붙여**, 사각형에 들어온 순서가 그대로 집합 순서가 된다.
    private bool RefreshBoxHits()
    {
        var rect = BoxSelectScreenRect;
        bool changed = false;

        for (int i = _boxHits.Count - 1; i >= 0; i--)
        {
            var e = _boxHits[i];
            if (e.Anchor != null && IsInsideBox(e.Anchor.position, rect)) continue;
            _boxHits.RemoveAt(i); // 사각형에서 빠졌거나 그새 파괴됐다
            changed = true;
        }

        var entries = GroupSelectableRegistry.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Anchor == null) continue;              // 파괴 대기 중인 항목
            if (!IsInsideBox(e.Anchor.position, rect)) continue;
            if (ContainsTarget(e.Target)) continue;      // 이미 담겨 있으면 순서를 유지한다

            _boxHits.Add(e);
            changed = true;
        }

        if (!changed) return false;

        // 통지용 목록은 내용이 바뀔 때만 다시 만든다(드래그 내내 매 프레임 새로 만들지 않는다).
        _boxTargets.Clear();
        for (int i = 0; i < _boxHits.Count; i++) _boxTargets.Add(_boxHits[i].Target);
        return true;
    }

    private bool ContainsTarget(IGroupSelectable target)
    {
        for (int i = 0; i < _boxHits.Count; i++)
        {
            if (ReferenceEquals(_boxHits[i].Target, target)) return true;
        }
        return false;
    }

    /// 월드 좌표를 스크린으로 투영해 사각형 포함 여부를 본다.
    /// 가림(지형·건물 뒤)은 따지지 않는다 — 후보마다 레이캐스트를 쏘지 않기 위한 의도된 단순화(RTS 표준).
    private bool IsInsideBox(Vector3 worldPos, Rect rect)
    {
        if (_camera == null) return false;

        Vector3 sp = _camera.WorldToScreenPoint(worldPos);
        if (sp.z <= 0f) return false; // 카메라 뒤 — 좌표가 뒤집혀 엉뚱하게 걸린다

        return rect.Contains(new Vector2(sp.x, sp.y));
    }

    private static Rect MakeRect(Vector2 a, Vector2 b)
    {
        return Rect.MinMaxRect(
            Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
            Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // ── Idle: 호버 (툴팁) ─────────────────────────────────────────
    // 커서 밑 IHoverable을 추적해 바뀔 때만 통지. 표시 여부·연출은 구독자(툴팁 UI) 책임.
    // 호버 대상 레이어는 선택 후보와 같다고 보고 _selectableMask를 재사용(최종 판정은 IHoverable 유무).
    private void UpdateHover(Vector2 screenPos, bool overUI)
    {
        IHoverable next = null;
        if (!overUI && RaycastMask(screenPos, _selectableMask, out var hit))
            hit.collider.TryGetComponent(out next); // 없으면 next는 null

        SetHover(next);
    }

    private void SetHover(IHoverable next)
    {
        if (ReferenceEquals(_hovered, next)) return;
        if (IsAlive(_hovered)) _hovered.OnHoverExit();
        _hovered = next;
        _hovered?.OnHoverEnter();
        OnHoverChanged?.Invoke(_hovered);
    }

    private void ClearHover() => SetHover(null);

    /// 단일 선택 + 그룹 선택을 함께 비우는 **선택 해제의 유일한 창구**. Esc·배치 시작·페이즈 전환이 공유한다.
    /// 두 신호를 함께 보내는 이유는 선택에 딸린 표시가 정보 패널 하나가 아니라 사거리 원·아웃라인·합성 패널까지
    /// 퍼져 있고, 그 소유자도 대상 자신(ISelectable 훅)과 코디네이터(그룹)로 나뉘어 있기 때문이다.
    /// OnPrimarySelect는 중복 제거를 타지 않으므로 Shift로만 선택한 상태(_selected==null)에서도 그룹이 풀린다(WL-085).
    ///
    /// ⚠️ 선택 상태를 "표시만" 내리고 _selected를 남기면 그 대상은 **재클릭해도 다시 뜨지 않는다**
    /// (Select의 `_selected == next` 중복 제거가 삼킨다). 표시를 내려야 하는 곳은 반드시 이 메서드를 쓸 것.
    public void ClearSelection()
    {
        Select(null);
        OnPrimarySelect?.Invoke(null);
    }

    private void Select(ISelectable next)
    {
        if (_selected == next) return;
        if (IsAlive(_selected)) _selected.OnDeselected();
        _selected = next;
        _selected?.OnSelected();
        OnSelectionChanged?.Invoke(_selected);
    }

    // 이전 대상이 그새 파괴됐을 수 있다(합성 소모·철거·사망). ISelectable/IHoverable은 **인터페이스** 타입이라
    // Unity의 파괴 감지(오버로드된 ==)를 타지 않고, C#의 `?.`는 순수 참조 null 검사라 죽은 대상도 통과시킨다
    // → 그대로 호출하면 대상 내부에서 MissingReferenceException이 난다(합성 후 타워 재선택 시 실제 발생).
    // HandleSceneLoaded가 필드만 리셋하는 것과 같은 계열의 함정(WL-033) — 통지 전에 생존을 확인한다.
    private static bool IsAlive(object target)
    {
        if (target == null) return false;
        if (target is Component component) return component != null; // Unity 오버로드 == 파괴 감지
        return true;
    }

    // ── Placement: 배치 (요구사항 ①) ──────────────────────────────
    private void UpdatePlacement(Vector2 screenPos, bool overUI)
    {
        // 우클릭/Esc 로 취소
        if (Mouse.current.rightButton.wasPressedThisFrame ||
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            CancelPlacement();
            return;
        }

        if (!RaycastMask(screenPos, _placementMask, out var hit)) return;

        Vector3 pos = _request.Snap != null ? _request.Snap(hit) : hit.point; // 스냅은 요청이 결정(그리드 스냅)
        _ghost.transform.position = pos;

        bool valid = _request.CanPlaceAt(hit);
        // TODO(하이라이트/연출 미확정): 고스트를 유효=초록/무효=빨강 등으로 표시

        if (!overUI && valid && Mouse.current.leftButton.wasPressedThisFrame)
        {
            _request.OnConfirmed(hit, pos);
            if (!_request.KeepPlacingAfterConfirm) CancelPlacement();
        }
    }

    // ── SkillTargeting: 스킬 범위 지정 (요구사항 ③, #103) ─────────
    // 전투 타일 위이기만 하면(종류 무관) 시전 가능하다. 고스트는 전투 타일 위에서만 표시하고,
    // 타일 밖(빈 칸·틈·맵 밖)에서는 숨긴다. 고스트를 실제 히트 표면(hit)에 붙이므로
    // 도로처럼 낮게 모델링된 타일 위에서도 표면에 자연스럽게 앉는다.
    private void UpdateSkillTargeting(Vector2 screenPos, bool overUI)
    {
        // 우클릭/Esc 로 취소
        if (Mouse.current.rightButton.wasPressedThisFrame ||
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            CancelSkillTargeting();
            return;
        }

        CombatMapTileView tile = null;
        if (RaycastMask(screenPos, _placementMask, out var hit))
            tile = hit.collider.GetComponentInParent<CombatMapTileView>();

        // 전투 타일이 아니면(빈 칸·타일 사이 틈·맵 밖) 인디케이터를 숨긴다.
        if (tile == null)
        {
            if (_ghost.activeSelf) _ghost.SetActive(false);
            return;
        }

        if (!_ghost.activeSelf) _ghost.SetActive(true);
        Ray ray = _camera.ScreenPointToRay(screenPos);
        Vector3 pos = _skillRequest.Snap != null ? _skillRequest.Snap(ray, hit) : hit.point;
        _ghost.transform.position = pos;

        // 전투 타일 위이면 시전한다. 타일 밖은 위에서 이미 고스트를 숨기고 return 했다.
        if (!overUI && Mouse.current.leftButton.wasPressedThisFrame)
        {
            _skillRequest.OnConfirmed(pos);
            CancelSkillTargeting(); // 한 번 시전하면 조준 모드 종료(연속 시전 불필요)
        }
    }

    // ── 레이캐스트 ────────────────────────────────────────────────
    // 그리드 스냅은 각 배치물이 PlacementRequest.Snap으로 제공한다(매니저는 배치 규칙을 모른다).
    private bool RaycastMask(Vector2 screenPos, LayerMask mask, out RaycastHit hit)
    {
        hit = default;
        if (_camera == null)
        {
            return false;
        }

        var ray = _camera.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out hit, Mathf.Infinity, mask);
    }
}
