
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{



    [Header("References")]
    [SerializeField] private CinemachineCamera cinemachineCamera;
    [SerializeField] private Transform cameraTarget;

    [Header("Move")]
    [SerializeField] private float moveSpeed = 15f;
    [SerializeField] private float edgeSize = 20f;
    [SerializeField] private Vector2 xBounds = new Vector2(-40f, 10f);
    [SerializeField] private Vector2 zBounds = new Vector2(0f, 50f);

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 2f;
    [SerializeField] private float minZoomSize  = 3f;
    [SerializeField] private float maxZoomSize = 20f;

    private void Update()
    {
        if (Mouse.current == null)
        {
            return;
        }

        MoveScreen();
        ZoomMouseWheel();
    }

    private void MoveScreen()
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Vector3 moveDirection = Vector3.zero;

        if (Keyboard.current == null)
        {
            return;
        }

        Transform mainCameraTransform = Camera.main.transform;

        Vector3 forward = mainCameraTransform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = mainCameraTransform.right;
        right.y = 0f;
        right.Normalize();

        if (Keyboard.current.wKey.isPressed)
        {
            moveDirection += forward;
        }

        if (Keyboard.current.sKey.isPressed)
        {
            moveDirection -= forward;
        }

        if (Keyboard.current.aKey.isPressed)
        {
            moveDirection -= right;
        }

        if (Keyboard.current.dKey.isPressed)
        {
            moveDirection += right;
        }


        bool isEdgeMoving = false;

        if (mousePosition.x <= edgeSize)
        {
            moveDirection -= right;
            isEdgeMoving = true;
        }
        else if (mousePosition.x >= Screen.width - edgeSize)
        {
            moveDirection += right;
            isEdgeMoving = true;
        }

        if (mousePosition.y <= edgeSize)
        {
            moveDirection -= forward;
            isEdgeMoving = true;
        }
        else if (mousePosition.y >= Screen.height - edgeSize)
        {
            moveDirection += forward;
            isEdgeMoving = true;
        }

        if (moveDirection == Vector3.zero)
        {
            return;
        }

        float currentMoveSpeed = isEdgeMoving ? moveSpeed + 5f : moveSpeed;

        Vector3 nextPosition =
            cameraTarget.position + moveDirection.normalized * currentMoveSpeed * Time.deltaTime;

        cameraTarget.position = ClampPosition(nextPosition);
    }

    private void ZoomMouseWheel()
    {
        float scrollValue = Mouse.current.scroll.ReadValue().y;

        if (Mathf.Approximately(scrollValue, 0f))
        {
            return;
        }

        float before = cinemachineCamera.Lens.OrthographicSize;

        float nextSize = Mathf.Clamp(
            before - scrollValue * zoomSpeed,
            minZoomSize,
            maxZoomSize
        );

        cinemachineCamera.Lens.OrthographicSize = nextSize;
    }

    private Vector3 ClampPosition(Vector3 position)
    {
        position.x = Mathf.Clamp(position.x, xBounds.x, xBounds.y);
        position.z = Mathf.Clamp(position.z, zBounds.x, zBounds.y);
        return position;
    }
}

