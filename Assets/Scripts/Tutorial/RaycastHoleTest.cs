using UnityEngine;
using UnityEngine.UI;

public class RaycastHoleTest : Image, ICanvasRaycastFilter
{
    public RectTransform hole;

    public bool IsRaycastLocationValid(Vector2 sp, Camera cam)
    {
        if (hole == null) return true;
        return !RectTransformUtility.RectangleContainsScreenPoint(hole, sp, cam);
    }
}