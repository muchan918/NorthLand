using FlatKit;
using UnityEngine;

// #148 픽셀레이션 해상도를 오쏘 줌에 연동한다(Docs/Rendering/VisualLookPipeline.md §3.7).
//
// 왜 필요한가: FlatKit Pixelation의 resolution은 "화면 긴 변의 픽셀 수"라 블록 크기가 화면 기준으로
// 고정된다. 오쏘 카메라를 줌 아웃하면 같은 블록이 더 넓은 월드를 담아 형태가 뭉개진다.
// 본진 실측(1920 기준): ortho 135 + resolution 320이면 블록이 월드 1.5유닛을 담아 건물이 식별되지 않고,
// resolution 480(= 1.0유닛)이면 형태가 유지된다. 즉 판단 기준은 화면 픽셀이 아니라 블록의 월드 크기다.
//
// 왜 settings.resolution을 쓰지 않고 머티리얼에 직접 쓰는가: FlatKit의 SetMaterialProperties()가
// private이고 Create()/에디터 OnValidate에서만 호출된다. 런타임에 settings.resolution만 바꿔도
// 반영되지 않는다. 그래서 노출된 settings.effectMaterial에 _PixelSize를 직접 쓴다(벤더 무수정).
//
// ⚠️ effectMaterial은 PixelationSettings 에셋의 서브에셋이다. 이 컴포넌트가 값을 몰면
// 그 에셋의 _PixelSize가 git diff에 뜬다 — 런타임에 매번 덮어쓰는 값이라 무해하다.
[ExecuteAlways]
[AddComponentMenu("NorthLand/Rendering/Pixelation Zoom Binder")]
public class PixelationZoomBinder : MonoBehaviour
{
    public enum Mode
    {
        // 블록이 담는 월드 크기를 일정하게 유지한다(resolution ∝ orthoSize).
        // 줌 아웃할수록 화면 블록이 작아져 픽셀감이 옅어진다.
        WorldLocked,

        // resolution을 고정한다(FlatKit 기본 동작). 줌 아웃하면 뭉개진다 — A/B 비교용.
        FixedResolution,
    }

    [Header("References")]
    [Tooltip("PC_Renderer에 등재한 Flat Kit Pixelation이 참조하는 설정 에셋")]
    [SerializeField] private PixelationSettings settings;

    [Tooltip("비우면 Camera.main을 쓴다")]
    [SerializeField] private Camera targetCamera;

    [Header("Mode")]
    [SerializeField] private Mode mode = Mode.WorldLocked;

    [Header("World Locked")]
    [Tooltip("블록 하나가 담는 월드 크기. 본진 기준 1.0이 형태 가독성의 하한이다(1.5면 뭉개진다).")]
    [Min(0.01f)]
    [SerializeField] private float blockWorldSize = 1.0f;

    [Tooltip("연동 결과를 이 범위로 제한한다. 상한을 낮추면 최대 축소에서도 픽셀감이 남지만 뭉개짐이 돌아온다.")]
    [SerializeField] private int minResolution = 160;

    [SerializeField] private int maxResolution = 2000;

    [Header("Fixed Resolution")]
    [Min(1)]
    [SerializeField] private int fixedResolution = 480;

    [Header("Debug (읽기 전용)")]
    [SerializeField] private int currentResolution;

    [SerializeField] private float currentBlockScreenPixels;

    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");

    private float _lastApplied = -1f;

    /// <summary>지금 프레임에 적용된 resolution. 캡처 스크립트가 값을 확인할 때 쓴다.</summary>
    public int CurrentResolution => currentResolution;

    private void LateUpdate()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void Apply()
    {
        if (settings == null || settings.effectMaterial == null)
        {
            return;
        }

        Camera cam = targetCamera != null ? targetCamera : Camera.main;

        if (cam == null)
        {
            return;
        }

        int resolution = mode == Mode.FixedResolution
            ? Mathf.Max(1, fixedResolution)
            : ResolutionForOrthoSize(cam);

        currentResolution = resolution;

        float longSide = Mathf.Max(cam.pixelWidth, cam.pixelHeight);
        currentBlockScreenPixels = longSide / resolution;

        float pixelSize = Mathf.Max(1f / resolution, 0.0001f);

        // 매 프레임 SetFloat을 때리지 않는다 — 서브에셋이라 불필요한 dirty를 만들 이유가 없다.
        if (Mathf.Approximately(pixelSize, _lastApplied))
        {
            return;
        }

        settings.effectMaterial.SetFloat(PixelSizeId, pixelSize);
        _lastApplied = pixelSize;
    }

    /// <summary>
    /// 블록의 월드 크기를 blockWorldSize로 유지하는 resolution.
    ///
    /// resolution은 '긴 변'의 픽셀 수이므로 세로 방향 블록 수는 resolution * (h / longSide)다.
    /// 오쏘 카메라가 보는 월드 높이는 2 * orthographicSize이므로
    ///   blockWorldSize = 2 * orthoSize * longSide / (resolution * h)
    /// 이를 resolution에 대해 풀면 아래 식이 된다.
    /// (검산: 1920x1080 · orthoSize 135 · blockWorldSize 1.0 → 480)
    /// </summary>
    private int ResolutionForOrthoSize(Camera cam)
    {
        float h = Mathf.Max(1f, cam.pixelHeight);
        float longSide = Mathf.Max(cam.pixelWidth, cam.pixelHeight);

        // 퍼스펙티브 카메라는 orthographicSize가 의미 없다 — 고정값으로 폴백한다.
        if (!cam.orthographic)
        {
            return Mathf.Max(1, fixedResolution);
        }

        float raw = 2f * cam.orthographicSize * longSide / (h * blockWorldSize);

        return Mathf.Clamp(Mathf.RoundToInt(raw), Mathf.Max(1, minResolution), Mathf.Max(1, maxResolution));
    }
}
