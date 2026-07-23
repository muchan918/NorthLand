using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class MinimapCameraBounds : MonoBehaviour
{
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private Camera targetCamera;

    [Header("Zoom Size")]
    [SerializeField] private float widthScale = 1f;
    [SerializeField] private float heightScale = 1f;

    [Header("Minimap Rendering")]
    [SerializeField] private float worldY = 500f;

    [Header("Position Offset")]
    [SerializeField] private float xOffset = 80f;
    [SerializeField] private float zOffset = 120f;

    private LineRenderer lineRenderer;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        lineRenderer.positionCount = 4;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = true;

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void LateUpdate()
    {
        if (cameraTarget == null ||targetCamera == null)
        {
            return;
        }

        float centerX = cameraTarget.position.x + xOffset;

        float centerZ = cameraTarget.position.z + zOffset;

        float halfHeight = targetCamera.orthographicSize * heightScale;

        float halfWidth = targetCamera.orthographicSize * targetCamera.aspect *widthScale;

        lineRenderer.SetPosition(0,new Vector3(centerX - halfWidth,worldY,centerZ - halfHeight));

        lineRenderer.SetPosition(1,new Vector3(centerX - halfWidth,worldY,centerZ + halfHeight));

        lineRenderer.SetPosition(2,new Vector3(centerX + halfWidth,worldY,centerZ + halfHeight));

        lineRenderer.SetPosition(3,new Vector3(centerX + halfWidth,worldY,centerZ - halfHeight));
    }
}