using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 영토 확장 구조(#67) 확인용 쿼터뷰 카메라. 기존 CameraController(다른 공간 정본)는 건드리지 않고
// 별도 Cinemachine 가상 카메라를 새로 만들어 이걸로 제어한다. 카메라 구도가 정식으로 정해지면
// 교체될 수 있는 임시 성격의 컨트롤러.
public class CameraController2 : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CinemachineCamera cinemachineCamera;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private WaveRewardSelectionUI waveRewardSelectionUI;

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

    private bool _isDragging;
    private Vector2 _dragStartScreenPos;
    private Vector3 _dragStartTargetPos;


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

        if (waveRewardSelectionUI == null)
        {
            Debug.LogWarning(
                "CameraController2: WaveRewardSelectionUI가 연결되지 않았습니다. " +
                "보상 선택 중 카메라 정지 기능이 작동하지 않습니다.",
                this);
        }

        if (hasMissingReference)
        {
            enabled = false;
        }
    }

    private void Update()
    {
        if (Mouse.current == null)
        {
            return;
        }

        if (waveRewardSelectionUI != null && waveRewardSelectionUI.Camerastop)
        {
            return;
        }

        MoveKeyboard();
        MoveDrag();
        ZoomMouseWheel();
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

        Vector3 nextPosition = cameraTarget.position + moveDirection.normalized * moveSpeed * Time.unscaledDeltaTime;
        cameraTarget.position = ClampPosition(nextPosition);
    }

    private void MoveDrag()
    {
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

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
        Vector2 screenDelta = _dragStartScreenPos - currentScreenPos; // 드래그 반대 방향으로 카메라 이동(잡아끄는 느낌)

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
        var lens = cinemachineCamera.Lens;
        lens.OrthographicSize = nextSize;
        cinemachineCamera.Lens = lens;
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

    private Vector3 ClampPosition(Vector3 position)
    {
        position.x = Mathf.Clamp(position.x, xBounds.x, xBounds.y);
        position.z = Mathf.Clamp(position.z, zBounds.x, zBounds.y);
        return position;
    }
}
