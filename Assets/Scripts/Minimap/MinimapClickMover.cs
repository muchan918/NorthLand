using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class MinimapClickMover :MonoBehaviour,IPointerClickHandler,IDragHandler
{
    [SerializeField] private Camera minimapCamera;
    [SerializeField] private CameraController2 cameraController2;

    [Header("실제 맵 바닥 높이")]
    [SerializeField] private float groundY = 0f;

    [SerializeField]
    private float offset = 0.08f;

    private RawImage rawImage;
    private RectTransform rectTransform;

    private void Awake()
    {
        rawImage = GetComponent<RawImage>();
        rectTransform = GetComponent<RectTransform>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        MoveCamera(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        MoveCamera(eventData);
    }

    private void MoveCamera(PointerEventData eventData)
    {
        // 오른쪽 마우스만 허용
        if (eventData.button != PointerEventData.InputButton.Right)
        {
            return;
        }

        if (minimapCamera == null || cameraController2 == null)
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,eventData.position,eventData.pressEventCamera,out Vector2 localPoint))
        {
            return;
        }

        Rect rect = rectTransform.rect;

        float u = Mathf.InverseLerp(rect.xMin,rect.xMax,localPoint.x);

        float v = Mathf.InverseLerp(rect.yMin,rect.yMax,localPoint.y);

        // 드래그 중 미니맵 밖으로 나가도 좌표 제한
        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);

        Rect uvRect = rawImage.uvRect;

        u = uvRect.x + u * uvRect.width;
        v = uvRect.y + v * uvRect.height- offset;

        Ray ray = minimapCamera.ViewportPointToRay(new Vector3(u, v , 0f));

        Plane groundPlane = new Plane(Vector3.up,new Vector3(0f, groundY, 0f));

        if (groundPlane.Raycast(ray, out float distance))
        {
            cameraController2.MoveTo(ray.GetPoint(distance));
        }
    }
}