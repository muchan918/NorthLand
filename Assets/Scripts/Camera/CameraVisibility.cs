using UnityEngine;

/// <summary>
/// "지금 이게 카메라에 보이는가"를 묻는 공용 창구(#138).
/// <br/>
/// <b>프러스텀 평면을 프레임당 1회만 계산해 캐시하는 것</b>이 이 클래스의 존재 이유다 — 질의하는 쪽이
/// 스포너 1개 + 비행 중인 열기구 N대로 늘어나는데, 각자 계산하면 같은 값을 개수만큼 반복해서 만든다.
/// <br/>
/// 카메라를 찾지 못하면 <b>"보인다"로 답한다.</b> 안 보인다고 답하면 연출이 조용히 사라져 원인을 찾기
/// 어려워지지만, 보인다고 답하면 최악이라도 "쓸데없이 살아 있는 오브젝트" 정도로 끝난다.
/// </summary>
public static class CameraVisibility
{
    private static readonly Plane[] s_planes = new Plane[6];

    private static Camera s_camera;
    private static int s_stampedFrame = -1;
    private static bool s_hasCamera;

    /// <summary>
    /// 반경 <paramref name="radius"/>의 구가 카메라 시야에 걸치는가.
    /// 여유를 두고 싶으면 실제 크기보다 큰 반경을 넘긴다(화면 밖에서 미리 등장/유지시키는 용도).
    /// </summary>
    public static bool IsVisible(Vector3 center, float radius)
    {
        if (!EnsurePlanes())
        {
            return true;
        }

        return GeometryUtility.TestPlanesAABB(s_planes, new Bounds(center, Vector3.one * (radius * 2f)));
    }

    private static bool EnsurePlanes()
    {
        if (s_stampedFrame == Time.frameCount)
        {
            return s_hasCamera;
        }

        s_stampedFrame = Time.frameCount;

        // 씬 전환·도메인 리로드로 참조가 죽을 수 있어 매번 확인한다(Camera.main은 Unity가 자체 캐시한다).
        if (s_camera == null)
        {
            s_camera = Camera.main;
        }

        s_hasCamera = s_camera != null;

        if (s_hasCamera)
        {
            GeometryUtility.CalculateFrustumPlanes(s_camera, s_planes);
        }

        return s_hasCamera;
    }
}
