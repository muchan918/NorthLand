using System;
using NorthLand.Core;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 경영 공간 쿼터뷰 카메라(#67에서 영토 확장 구조 확인용으로 출발했고, 영토 시스템이 빠진 지금은
// GameScene의 실제 카메라 컨트롤러다, #337). 기존 CameraController(다른 공간 정본)는 건드리지 않고
// 별도 Cinemachine 가상 카메라를 새로 만들어 이걸로 제어한다. 카메라 구도가 정식으로 정해지면
// 교체될 수 있는 임시 성격의 컨트롤러.

public class CameraController2 : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CinemachineCamera cinemachineCamera;
    [SerializeField] private Transform cameraTarget;

    [Header("Move (WASD)")]
    [SerializeField] private float moveSpeed = 15f;
    [SerializeField] private Vector2 xBounds = new Vector2(-40f, 40f);
    [SerializeField] private Vector2 zBounds = new Vector2(-40f, 40f);

    [Header("Move (Drag)")]
    // 우클릭 드래그 — 좌클릭(MouseManager의 선택 입력)과 버튼을 분리해, 드래그 시작 지점의
    // 오브젝트가 선택/해제되던 부작용을 없앴다. 단 우클릭은 배치/스킬 지정 모드에서 취소로도
    // 쓰이므로(MouseManager), 그 모드 중 드래그하면 취소와 함께 카메라가 끌릴 수 있다.
    [SerializeField] private float dragSpeed = 0.05f;

    [Header("Zoom (Orthographic Size)")]
    [SerializeField] private float zoomSpeed = 2f;
    [SerializeField] private float minZoomSize = 6f;
    [SerializeField] private float maxZoomSize = 35f;

    // 줌 범위를 읽기 전용으로 공개한다. 룩 계층(#148 PixelationZoomBinder)이 현재 줌을
    // 0~1로 정규화하는 데 쓴다 — 여기서 범위를 바꾸면 그쪽 설정을 따로 고칠 필요가 없다.
    public float MinZoomSize => minZoomSize;

    public float MaxZoomSize => maxZoomSize;

    /// <summary>
    /// 줌이 실제로 바뀌었을 때 발생(#138). 인자는 <b>변경 후 오쏘 사이즈</b>다 — 변화량이 아니다.<br/>
    /// 소비처(건물 아웃라인·머리 위 아이콘·주민 이모티콘 등)는 전부 "지금 얼마나 축소돼 있는가"로
    /// 판단하므로 절대값이 바로 쓰인다. 변화량만 실어 보내면 각 소비처가 자기 누적값을 따로 들어야 하고,
    /// <b>게임 도중 생성된 오브젝트가 초기 상태를 영영 알 수 없다</b>.<br/>
    /// 그래서 계약은 "붙을 때 <see cref="CurrentZoomSize"/>로 pull, 바뀌면 이 이벤트로 push"다
    /// (<c>ManagementController.OnChanged</c>와 같은 계보).<br/>
    /// <br/>
    /// ⚠ 발행처가 <b>둘</b>이다 — 마우스 휠(<see cref="ZoomMouseWheel"/>)과 대상 줌(<see cref="UpdateTargetZoom"/>).
    /// 세이브 복원처럼 <c>Lens</c>를 직접 쓰는 경로를 더 추가하면 그쪽에서도 발행해야 소비처가 어긋나지 않는다.
    /// 발행이 두 곳에 인라인으로 복제돼 있어 이 경고가 유일한 방어다(WL-024의 <c>ApplyZoom</c> seam이 아직 없다).
    /// </summary>
    public event Action<float> OnZoomChanged;

    /// <summary>현재 오쏘 사이즈. 구독 시점에 1회 읽어 초기 상태를 맞추는 용도다.</summary>
    public float CurrentZoomSize =>
        cinemachineCamera != null ? cinemachineCamera.Lens.OrthographicSize : minZoomSize;

    [SerializeField] private float zoomSmoothTime = 0.2f;

    private bool isZooming;
    private float zoomTarget;
    private float zoomVelocity;

    [Header("미니맵 이동")]
    [SerializeField] private float minimapMoveSmoothTime = 0.15f;

    [SerializeField] private Camera mainCamera;

    private Vector3 minimapMoveTarget;
    private Vector3 minimapMoveVelocity;
    private bool isMinimapMoving;

    private bool _isDragging;
    private Vector2 _dragStartScreenPos;
    private Vector3 _dragStartTargetPos;

    [Header("UI References")]
    [SerializeField]
    private WaveRewardSelectionUI waveRewardSelectionUI;

    [SerializeField]
    private SettingUI settingUI;

    [SerializeField]
    private GameResult gameResult;



    private void Awake()
    {
        bool hasMissingReference = false;

        if (cinemachineCamera == null)
        {
            Debug.LogError(
                "CameraController2: Cinemachine Camera 참조가 할당되지 않았습니다.",
                this);

            hasMissingReference = true;
        }

        if (cameraTarget == null)
        {
            Debug.LogError(
                "CameraController2: Camera Target 참조가 할당되지 않았습니다.",
                this);

            hasMissingReference = true;
        }



        if (hasMissingReference)
        {
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        UpdateTargetZoom();

        if (!isMinimapMoving || cameraTarget == null)
        {
            return;
        }

        cameraTarget.position = Vector3.SmoothDamp(cameraTarget.position, minimapMoveTarget, ref minimapMoveVelocity, minimapMoveSmoothTime,
       Mathf.Infinity, Time.unscaledDeltaTime);

        if (Vector3.SqrMagnitude(cameraTarget.position - minimapMoveTarget) < 0.01f)
        {
            cameraTarget.position = minimapMoveTarget;
            minimapMoveVelocity = Vector3.zero;
            isMinimapMoving = false;
        }
    }

    private void Update()
    {
        bool rewardPanelOpen = waveRewardSelectionUI != null && waveRewardSelectionUI.Camerastop;

        bool settingPanelOpen = settingUI != null && settingUI.IsOpen;

        if (rewardPanelOpen || settingPanelOpen)
        {
            _isDragging = false;
            CancelMinimapMove();
            return;
        }

        if (Mouse.current == null)
        {
            return;
        }

        if (GameManager.Instance.Result != GameResult.Playing)
        {
            return;
        }

        MoveKeyboard();
        MoveDrag();
        ZoomMouseWheel();
    }

    private void UpdateTargetZoom()
    {
        if (!isZooming || cinemachineCamera == null) return;

        float next = Mathf.SmoothDamp(cinemachineCamera.Lens.OrthographicSize, zoomTarget,
            ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

        if (Mathf.Abs(next - zoomTarget) < 0.01f)
        {
            next = zoomTarget;
            zoomVelocity = 0f;
            isZooming = false;
        }

        // 렌즈 대입 방식은 ZoomMouseWheel(242~256행)과 똑같이 맞출 것.
        var lens = cinemachineCamera.Lens;
        lens.OrthographicSize = next;
        cinemachineCamera.Lens = lens;

        OnZoomChanged?.Invoke(next);
    }

    private void MoveKeyboard()
    {
        if (Keyboard.current == null)
        {
            return;
        }



        Vector3 moveDirection = Vector3.zero;

        Vector3 forward = GroundForward();
        Vector3 right = GroundRight();

        if (Keyboard.current.wKey.isPressed) moveDirection += forward;
        if (Keyboard.current.sKey.isPressed) moveDirection -= forward;
        if (Keyboard.current.aKey.isPressed) moveDirection -= right;
        if (Keyboard.current.dKey.isPressed) moveDirection += right;

        if (moveDirection == Vector3.zero)
        {
            return;
        }

        CancelMinimapMove();

        Vector3 nextPosition = cameraTarget.position + moveDirection.normalized * moveSpeed * Time.unscaledDeltaTime;
        cameraTarget.position = ClampPosition(nextPosition);
    }

    private void MoveDrag()
    {
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            // UI 위에서 누른 경우 큰 맵 드래그를 시작하지 않음
            if (EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            // 수동 드래그를 시작하면 미니맵 자동 이동 취소
            CancelMinimapMove();

            _isDragging = true;
            _dragStartScreenPos = Mouse.current.position.ReadValue();

            _dragStartTargetPos = cameraTarget.position;
        }
        else if (Mouse.current.rightButton.wasReleasedThisFrame)
        {
            _isDragging = false;
        }

        if (!_isDragging)
        {
            return;
        }

        Vector2 currentScreenPos = Mouse.current.position.ReadValue();

        // 드래그 반대 방향으로 카메라 이동
        Vector2 screenDelta = _dragStartScreenPos - currentScreenPos;

        Vector3 offset = (GroundRight() * screenDelta.x + GroundForward() * screenDelta.y) * dragSpeed;

        cameraTarget.position = ClampPosition(_dragStartTargetPos + offset);
    }

    private void ZoomMouseWheel()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        float scrollValue = Mouse.current.scroll.ReadValue().y;

        if (Mathf.Approximately(scrollValue, 0f))
        {
            return;
        }

        float before = cinemachineCamera.Lens.OrthographicSize;
        float nextSize = Mathf.Clamp(before - scrollValue * zoomSpeed, minZoomSize, maxZoomSize);

        // 최대/최소에 붙은 채 휠을 계속 굴리면 클램프에 걸려 값이 그대로다 — 그때마다 발행하면
        // 구독자가 같은 값으로 매 프레임 헛돈다. "실제로 바뀌었을 때만" 알린다.
        if (Mathf.Approximately(before, nextSize))
        {
            return;
        }

        var lens = cinemachineCamera.Lens;
        lens.OrthographicSize = nextSize;
        cinemachineCamera.Lens = lens;

        OnZoomChanged?.Invoke(nextSize);
    }

    private Vector3 GroundForward()
    {
        Vector3 forward = cinemachineCamera.transform.forward;
        forward.y = 0f;
        return forward.normalized;
    }

    private Vector3 GroundRight()
    {
        Vector3 right = cinemachineCamera.transform.right;
        right.y = 0f;
        return right.normalized;
    }

    public void MoveTo(Vector3 worldPosition)
    {
        if (cameraTarget == null)
        {
            return;
        }

        worldPosition.y = cameraTarget.position.y;

        minimapMoveTarget = ClampPosition(worldPosition);
        isMinimapMoving = true;
    }

    /// 지정한 오쏘 사이즈로 부드럽게 줌한다. 범위 밖 값은 clamp된다.
    public void ZoomTo(float orthographicSize)
    {
        if (cinemachineCamera == null) return;

        zoomTarget = Mathf.Clamp(orthographicSize, minZoomSize, maxZoomSize);
        zoomVelocity = 0f;
        isZooming = true;
    }

    public void MoveViewCenterTo(
    Vector3 clickedWorldPosition,
    float groundY)
    {
        if (cameraTarget == null)
        {
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            MoveTo(clickedWorldPosition);
            return;
        }

        Plane groundPlane = new Plane(
            Vector3.up,
            new Vector3(0f, groundY, 0f));

        // 현재 메인 화면의 정중앙이 지면의 어디를 보고 있는지 계산
        Ray centerRay = mainCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f));

        if (!groundPlane.Raycast(centerRay, out float distance))
        {
            MoveTo(clickedWorldPosition);
            return;
        }

        Vector3 currentViewCenter =
            centerRay.GetPoint(distance);

        // cameraTarget과 실제 화면 중심 사이의 차이
        Vector3 targetOffset =
            cameraTarget.position - currentViewCenter;

        Vector3 correctedTargetPosition =
            clickedWorldPosition + targetOffset;

        correctedTargetPosition.y =
            cameraTarget.position.y;

        minimapMoveTarget =
            ClampPosition(correctedTargetPosition);

        isMinimapMoving = true;
    }

    // 대상 추종 모션(이동+줌)을 함께 내린다. 줌만 남기면 보상·설정 패널이 열렸을 때
    // 이동은 멎는데 줌만 계속 빨려 들어간다 — UpdateTargetZoom이 unscaledDeltaTime이라 timeScale=0에서도 돈다.
    private void CancelMinimapMove()
    {
        isMinimapMoving = false;
        minimapMoveVelocity = Vector3.zero;

        isZooming = false;
        zoomVelocity = 0f;

        if (cameraTarget != null)
        {
            minimapMoveTarget = cameraTarget.position;
        }
    }
    private Vector3 ClampPosition(Vector3 position)
    {
        position.x = Mathf.Clamp(position.x, xBounds.x, xBounds.y);
        position.z = Mathf.Clamp(position.z, zBounds.x, zBounds.y);
        return position;
    }
}
