using UnityEngine;

/// 주민 목소리가 **지금 얼마나 들려야 하는가**를 답하는 공용 창구(Resident.md §7.2 사운드).
/// `CameraVisibility`와 같은 자리·같은 이유다 — 질의 주체가 주민 수만큼 늘어나는데 카메라 조회와
/// 줌 판정은 전원이 같은 답을 쓰므로, **프레임당 1회만 계산해 캐시한다.**
///
/// ── 왜 Unity의 3D 오디오를 쓰지 않는가 ────────────────────────────────
///
/// 요구가 "월드 거리"가 아니라 **"화면 중심에서의 거리"**다. 그리고 이 카메라로는 3D 감쇠가 성립조차
/// 하지 않는다:
///
/// - **오쏘그래픽이라 원근이 없다.** 시선축을 따라 앞으로 당겨도 화면상 크기·위치가 안 변한다(§8.5).
///   깊이는 정렬에만 쓰이므로 "카메라에 가까운 주민"이라는 개념 자체가 화면과 무관하다.
/// - **`AudioListener`가 마을 위 463유닛**(Main Camera)에 떠 있다. 전원이 사실상 등거리라
///   `rolloff`를 어떻게 잡아도 화면 위치와 상관없는 소리가 된다.
///
/// 그래서 **2D로 재생하고 볼륨을 여기서 직접 계산한다.** 뷰포트 좌표가 곧 답이라 오쏘/원근에도
/// 흔들리지 않는다.
public static class ResidentVoiceAudibility
{
    // ── 줌 게이트 (요구 ①: 가까이 들여다볼 때만 사람 소리가 들린다) ──────
    //
    // 씬의 줌 범위는 **30~150**이므로 아래 구간은 확대 쪽 절반이다. 끝값에서 딱 끊지 않고 구간을 두는
    // 이유는 휠을 굴릴 때 소리가 뚝 끊기기 때문이다 — 경계에서 0에 **도달**하므로 "그 너머는 무음"은
    // 그대로 지켜진다.
    //
    // ⚠ **두 값은 플레이하며 귀로 맞춘 것이다.** 처음엔 40/50이었는데 체감 구간이 30~40밖에 안 됐다 —
    //   `SmoothStep`이 구간 뒷부분을 빠르게 떨어뜨려(중간값 0.5, 3/4 지점 0.156) 숫자상 범위보다
    //   들리는 범위가 좁아지기 때문이다. 끝값을 넓히는 것으로 맞췄다. **숫자만 보고 되돌리지 말 것.**
    private const float k_FullVolumeOrthoSize = 40f;   // 이 이하 = 줌 감쇠 없음
    private const float k_SilentOrthoSize = 80f;       // 이 이상 = 완전 무음

    /// 화면 경계까지의 거리를 1로 정규화했을 때, 이 값 이상이면 무음. 1이면 **화면 밖에서 정확히 0**이 된다.
    private const float k_EdgeSilenceAt = 1f;

    private static Camera s_camera;
    private static int s_stampedFrame = -1;
    private static float s_zoomGain;
    private static bool s_hasCamera;

    /// 이 위치의 주민 목소리가 들려야 하는 정도(0~1)와 좌우 팬(-1~1).
    ///
    /// 카메라를 못 찾으면 **들리지 않는 것으로 답한다.** `CameraVisibility`가 반대로(보인다고) 답하는 것과
    /// 다른 선택인데, 소리는 "화면 밖인데 들린다"가 곧바로 버그로 들리는 반면 시각물은 살아 있어도
    /// 화면에 안 나오면 그만이기 때문이다.
    public static bool TryEvaluate(Vector3 worldPosition, out float gain, out float pan)
    {
        gain = 0f;
        pan = 0f;

        if (!EnsureFrameState() || s_zoomGain <= 0f)
        {
            return false;
        }

        Vector3 viewport = s_camera.WorldToViewportPoint(worldPosition);

        // 카메라 뒤. 오쏘에서도 근평면 뒤는 좌표가 뒤집혀 엉뚱하게 걸린다(CameraVisibility와 같은 방어).
        if (viewport.z <= 0f)
        {
            return false;
        }

        float dx = viewport.x - 0.5f;
        float dy = viewport.y - 0.5f;

        // ⚠ 유클리드 거리가 아니라 **두 축 중 큰 쪽**을 쓴다.
        //
        // 화면은 16:9라 정사각이 아니다. 유클리드로 재면 0에 닿는 지점이 화면에 내접하는 원이 되어
        // **모서리 쪽은 아직 화면 안인데 이미 무음**이 되고, 반대로 좌우 끝은 경계를 넘는 순간까지 소리가
        // 남아 뚝 끊긴다. 큰 쪽을 쓰면 어느 방향으로 나가든 **경계에서 정확히 0**이라 요구 ②의
        // "카메라 밖에는 안 들린다"가 불연속 없이 성립한다.
        float edgeDistance = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) / 0.5f;

        if (edgeDistance >= k_EdgeSilenceAt)
        {
            return false;
        }

        // 양 끝이 평평한 곡선을 쓴다 — 중앙에서는 걸어 다녀도 볼륨이 출렁이지 않고,
        // 가장자리에서는 스며들듯 사라진다.
        float screenGain = Mathf.SmoothStep(0f, 1f, 1f - edgeDistance / k_EdgeSilenceAt);

        gain = s_zoomGain * screenGain;

        // 화면 좌우 위치를 그대로 팬으로 쓴다. 경계에서 ±1이 되도록 2배.
        pan = Mathf.Clamp(dx * 2f, -1f, 1f);

        return gain > 0f;
    }

    private static bool EnsureFrameState()
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

        if (!s_hasCamera)
        {
            return false;
        }

        // 줌 값을 `CameraController2`가 아니라 **카메라에서 직접 읽는다.** Cinemachine이 렌즈를 카메라에
        // 밀어 넣으므로 값은 같고, 컨트롤러가 없는 씬(주민 테스트 씬)에서도 그대로 동작한다.
        // 원근 카메라면 줌 게이트라는 개념이 없으므로 통과시킨다.
        s_zoomGain = s_camera.orthographic
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(k_SilentOrthoSize, k_FullVolumeOrthoSize, s_camera.orthographicSize))
            : 1f;

        return true;
    }
}
