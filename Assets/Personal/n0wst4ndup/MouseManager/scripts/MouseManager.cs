using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;   // 프로젝트는 신규 Input System 사용

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
    private Mode _mode = Mode.Idle;

    [SerializeField] Camera _camera;
    [SerializeField] LayerMask _worldMask;

    private ISelectable _selected;
    private PlacementRequest _request;
    private GameObject _ghost;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        Vector2 screenPos = Mouse.current.position.ReadValue();
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        switch (_mode)
        {
            case Mode.Idle: UpdateIdle(screenPos, overUI); break;
            case Mode.Placement: UpdatePlacement(screenPos, overUI); break;
        }
    }

    // ── 외부 진입점 ────────────────────────────────────────────────
    public void BeginPlacement(PlacementRequest request)
    {
        CancelPlacement();
        _request = request;
        _ghost = Instantiate(request.GhostPrefab);
        _mode = Mode.Placement;
    }

    public void CancelPlacement()
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _request = null;
        _mode = Mode.Idle;
    }

    // ── Idle: 선택 (요구사항 ②) ────────────────────────────────────
    void UpdateIdle(Vector2 screenPos, bool overUI)
    {
        if (overUI || !Mouse.current.leftButton.wasPressedThisFrame) return;

        if (Raycast(screenPos, out var hit) && hit.collider.TryGetComponent(out ISelectable sel))
            Select(sel);
        else
            Select(null); // 빈 곳 클릭 → 선택 해제
    }

    void Select(ISelectable next)
    {
        if (_selected == next) return;
        _selected?.OnDeselected();
        _selected = next;
        _selected?.OnSelected();
        OnSelectionChanged?.Invoke(_selected);
    }

    // ── Placement: 배치 (요구사항 ①) ──────────────────────────────
    void UpdatePlacement(Vector2 screenPos, bool overUI)
    {
        // 우클릭/Esc 로 취소
        if (Mouse.current.rightButton.wasPressedThisFrame ||
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            CancelPlacement();
            return;
        }

        if (!Raycast(screenPos, out var hit)) return;

        Vector3 pos = Snap(hit.point); // 그리드 스냅 (TBD)
        _ghost.transform.position = pos;

        bool valid = _request.CanPlaceAt(pos);
        // TODO(하이라이트/연출 미확정): 고스트를 유효=초록/무효=빨강 등으로 표시

        if (!overUI && valid && Mouse.current.leftButton.wasPressedThisFrame)
        {
            _request.OnConfirmed(pos);
            if (!_request.KeepPlacingAfterConfirm) CancelPlacement();
        }
    }

    // ── 레이캐스트 / 스냅 (구현 방식 TBD) ─────────────────────────
    private bool Raycast(Vector2 screenPos, out RaycastHit hit) // 3D 기준. 2D면 Physics2D.Raycast로 교체
    {
        var ray = _camera.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out hit, Mathf.Infinity, _worldMask);
    }

    private Vector3 Snap(Vector3 world) => world; // TODO: 그리드 좌표로 스냅
}