using System;
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

    // ── 외부 진입점 ────────────────────────────────────────────────
    public void BeginPlacement(PlacementRequest request)
    {
        CancelPlacement();
        CancelSkillTargeting();
        ClearHover();     // 배치 중에는 툴팁을 띄우지 않는다
        ClearSelection(); // 고스트를 드는 순간 이전 선택의 잔재(사거리 원·초록 아웃라인·인포/합성 패널)를 전부 내린다(WL-086)
        _request = request;
        _ghost = Instantiate(request.GhostPrefab);
        _mode = Mode.Placement;
    }

    public void CancelPlacement()
    {
        _request?.OnEnded?.Invoke(); // 배치 종료(취소/확정 복귀) 시 요청이 만든 프리뷰 등을 정리할 수 있게 통지
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _request = null;
        _mode = Mode.Idle;
    }

    // 스킬 타겟팅(#103): 그리드 스냅·점유 검증이 필요 없는 PlacementRequest의 경량 버전.
    // 클릭한 위치를 그대로 SkillTargetRequest.OnConfirmed로 넘긴다(요구사항: SystemMap §4 계약).
    public void BeginSkillTargeting(SkillTargetRequest request)
    {
        CancelPlacement();
        CancelSkillTargeting();
        ClearHover(); // 타겟팅 중에는 툴팁을 띄우지 않는다
        _skillRequest = request;
        _ghost = Instantiate(request.GhostPrefab);
        _mode = Mode.SkillTargeting;
    }

    public void CancelSkillTargeting()
    {
        _skillRequest?.OnEnded?.Invoke();
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _skillRequest = null;
        _mode = Mode.Idle;
    }

    // ── Idle: 선택 (요구사항 ②) ────────────────────────────────────
    private void UpdateIdle(Vector2 screenPos, bool overUI)
    {
        UpdateHover(screenPos, overUI);

        // Esc → 전체 해제(그룹 포함). 코디네이터가 OnSelectionChanged(null)을 받아 집합을 비운다.
        // (우클릭은 카메라 드래그·조준 취소와 이미 이중 점유라 해제에 쓰지 않는다 — WL-073)
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ClearSelection();
            return;
        }

        if (overUI || !Mouse.current.leftButton.wasPressedThisFrame) return;

        // 추가 선택 키(Shift) 판정은 입력 단일 창구인 MouseManager가 소유한다(계약 #1).
        bool additive = Keyboard.current != null &&
                        (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

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
    // 타일 밖(빈 칸·틈·맵 밖)에서는 숨긴다. 고스트를 실제 히트 표면(hit.point)에 붙이므로
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
        _ghost.transform.position = hit.point; // 실제 표면 → 도로면 낮게 앉음

        // 전투 타일 위이면 시전한다. 타일 밖은 위에서 이미 고스트를 숨기고 return 했다.
        if (!overUI && Mouse.current.leftButton.wasPressedThisFrame)
        {
            _skillRequest.OnConfirmed(hit.point);
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