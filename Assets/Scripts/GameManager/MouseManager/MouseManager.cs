using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;   // 프로젝트는 신규 Input System 사용
using UnityEngine.SceneManagement;

/// 클릭으로 선택 가능한 배치물(타워·건물 등)이 구현한다. (요구사항 ②)
public class MouseManager : MonoBehaviour
{
    enum Mode
    {
        Idle,
        Placement
    }

    public static MouseManager Instance { get; private set; }

    public event Action<ISelectable> OnSelectionChanged;
    // 커서 밑 호버 대상이 바뀔 때만 통지(없으면 null). 툴팁 UI가 구독해 표시/숨김을 결정한다.
    public event Action<IHoverable> OnHoverChanged;
    // 현재 포인터 화면 좌표. 다른 시스템(툴팁 등)이 Mouse.current를 직접 읽지 않고 여기서 얻는다(입력 단일 창구 계약).
    public Vector2 PointerPosition { get; private set; }

    private Mode _mode = Mode.Idle;

    [SerializeField] Camera _camera;

    [Header("Raycast Layers")]
    // 선택 후보 레이어(타워/건물/병사...). 최종 선택 여부는 ISelectable 유무로 판정하므로, 레이어는 굵은 필터일 뿐.
    [SerializeField] LayerMask _selectableMask;
    // 배치 표면 레이어(바닥/그리드). 고스트가 이 위에 올라간다.
    [SerializeField] LayerMask _placementMask;

    private ISelectable _selected;
    private IHoverable _hovered;
    private PlacementRequest _request;
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
        ClearHover(); // 배치 중에는 툴팁을 띄우지 않는다
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

    // ── Idle: 선택 (요구사항 ②) ────────────────────────────────────
    private void UpdateIdle(Vector2 screenPos, bool overUI)
    {
        UpdateHover(screenPos, overUI);

        if (overUI || !Mouse.current.leftButton.wasPressedThisFrame) return;

        if (RaycastMask(screenPos, _selectableMask, out var hit) && hit.collider.TryGetComponent(out ISelectable sel))
            Select(sel);
        else
            Select(null); // 빈 곳 클릭 → 선택 해제
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
        _hovered?.OnHoverExit();
        _hovered = next;
        _hovered?.OnHoverEnter();
        OnHoverChanged?.Invoke(_hovered);
    }

    private void ClearHover() => SetHover(null);

    private void Select(ISelectable next)
    {
        if (_selected == next) return;
        _selected?.OnDeselected();
        _selected = next;
        _selected?.OnSelected();
        OnSelectionChanged?.Invoke(_selected);
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