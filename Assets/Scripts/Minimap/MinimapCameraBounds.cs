using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class MinimapCameraBounds : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Camera minimapCamera;

    [Header("Ground")]
    [SerializeField] private float groundY = 0f;

    [Header("Line Rendering")]
    [SerializeField] private float distanceFromNearPlane = 1f;

    private LineRenderer lineRenderer;
    private Plane groundPlane;

    [Header("Rotation")]
    [SerializeField] private float rotationOffset;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        lineRenderer.positionCount = 4;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = true;

        groundPlane = new Plane(Vector3.up,new Vector3(0f, groundY, 0f));
    }

    private void LateUpdate()
    {
        if (targetCamera == null || minimapCamera == null)
        {
            return;
        }

        if (!TryGetGroundPoint(new Vector2(0f, 0f),out Vector3 bottomLeft) ||!TryGetGroundPoint(new Vector2(0f, 1f),out Vector3 topLeft) ||
            !TryGetGroundPoint(new Vector2(1f, 1f),out Vector3 topRight) ||!TryGetGroundPoint(new Vector2(1f, 0f),out Vector3 bottomRight))
        {
            return;
        }

        // 미니맵 카메라의 Near Plane보다 약간 앞에 선을 배치한다.
        Vector3 renderPosition =minimapCamera.transform.position +minimapCamera.transform.forward *(minimapCamera.nearClipPlane + distanceFromNearPlane);

        float renderY = renderPosition.y;

        Vector3 center =(bottomLeft + topLeft + topRight + bottomRight) / 4f;

        Quaternion rotation =Quaternion.Euler(0f, rotationOffset, 0f);

        bottomLeft = center + rotation * (bottomLeft - center);

        topLeft = center + rotation * (topLeft - center);

        topRight = center + rotation * (topRight - center);

        bottomRight = center + rotation * (bottomRight - center);

        bottomLeft.y = renderY;
        topLeft.y = renderY;
        topRight.y = renderY;
        bottomRight.y = renderY;

        lineRenderer.SetPosition(0, bottomLeft);
        lineRenderer.SetPosition(1, topLeft);
        lineRenderer.SetPosition(2, topRight);
        lineRenderer.SetPosition(3, bottomRight);
    }

    private bool TryGetGroundPoint(Vector2 viewportPoint,
        out Vector3 groundPoint)
    {
        Ray ray = targetCamera.ViewportPointToRay(new Vector3(viewportPoint.x,viewportPoint.y,0f));

        if (groundPlane.Raycast(ray, out float distance))
        {
            groundPoint = ray.GetPoint(distance);
            return true;
        }

        groundPoint = default;
        return false;
    }

    private void OnValidate()
    {
        distanceFromNearPlane =Mathf.Max(0.01f, distanceFromNearPlane);

        groundPlane = new Plane(Vector3.up,new Vector3(0f, groundY, 0f));
    }
}