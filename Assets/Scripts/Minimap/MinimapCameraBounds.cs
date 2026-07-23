using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class MinimapCameraBounds : MonoBehaviour
{
    [SerializeField] private Transform cameraTarget;

    [Header("Rectangle Size")]
    [SerializeField] private float halfWidth = 200f;
    [SerializeField] private float halfHeight = 200f;

    [Header("Minimap Rendering")]
    [SerializeField] private float worldY = 500f;

    private LineRenderer lineRenderer;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        lineRenderer.positionCount = 4;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = true;
    }

    private void LateUpdate()
    {
        if (cameraTarget == null)
        {
            return;
        }

        float centerX = cameraTarget.position.x;
        float centerZ = cameraTarget.position.z;

        lineRenderer.SetPosition(0,new Vector3(centerX - halfWidth,worldY,centerZ - halfHeight));

        lineRenderer.SetPosition(1,new Vector3(centerX - halfWidth,worldY,centerZ + halfHeight));

        lineRenderer.SetPosition(2,new Vector3(centerX + halfWidth,worldY,centerZ + halfHeight));

        lineRenderer.SetPosition(3,new Vector3(centerX + halfWidth,worldY,centerZ - halfHeight));
    }
}