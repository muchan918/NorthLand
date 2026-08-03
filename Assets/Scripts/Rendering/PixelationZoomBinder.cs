using FlatKit;
using Unity.Cinemachine;
using UnityEngine;

// #148 픽셀레이션 해상도를 오쏘 줌에 연동한다(Docs/Rendering/VisualLookPipeline.md §3.7).
//
// 왜 필요한가: FlatKit Pixelation의 resolution은 "화면 긴 변의 픽셀 수"라 블록 크기가 화면 기준으로
// 고정된다. 오쏘 카메라를 줌 아웃하면 같은 블록이 더 넓은 월드를 담아 형태가 뭉개진다.
// 고정값으로는 게임 줌 범위에서 룩을 평가할 수 없으므로, 픽셀 채택 판단의 선행 조건이다.
//
// 기본 모드가 NormalizedZoom인 이유: 줌 범위(CameraController2의 min/max)는 카메라 튜닝 중에
// 계속 바뀐다. 절대 기준(WorldLocked의 blockWorldSize)으로 잡아두면 범위를 건드릴 때마다
// 값을 다시 유도해야 한다. "최대 확대에서 이 해상도 / 최대 축소에서 이 해상도"로 잡아두면
// 범위가 바뀌어도 양 끝의 의미가 유지된다.
//
// 왜 settings.resolution을 쓰지 않고 머티리얼에 직접 쓰는가: FlatKit의 SetMaterialProperties()가
// private이고 Create()/에디터 OnValidate에서만 호출된다. 런타임에 settings.resolution만 바꿔도
// 반영되지 않는다. 그래서 노출된 settings.effectMaterial에 _PixelSize를 직접 쓴다(벤더 무수정).
//
// ⚠️ effectMaterial은 PixelationSettings 에셋의 서브에셋이다. 이 컴포넌트가 값을 몰면
// 그 에셋의 _PixelSize가 git diff에 뜬다 — 매 프레임 덮어쓰는 값이라 무해하다.
[ExecuteAlways]
[AddComponentMenu("NorthLand/Rendering/Pixelation Zoom Binder")]
public class PixelationZoomBinder : MonoBehaviour
{
    public enum Mode
    {
        // 줌 범위를 0~1로 정규화해 양 끝 해상도 사이를 보간한다. 줌 범위가 바뀌어도 설정이 유효하다.
        NormalizedZoom,

        // 블록이 담는 월드 크기를 일정하게 유지한다(resolution ∝ orthoSize).
        // 절대 기준이라 줌 범위를 바꾸면 blockWorldSize를 다시 잡아야 한다.
        WorldLocked,

        // resolution 고정(FlatKit 기본 동작). 줌 아웃하면 뭉개진다 — A/B 비교용.
        FixedResolution,
    }

    [Header("References")]
    [Tooltip("PC_Renderer에 등재한 Flat Kit Pixelation이 참조하는 설정 에셋")]
    [SerializeField] private PixelationSettings settings;

    [Tooltip("비우면 Camera.main을 쓴다")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("지정하면 현재 오쏘 사이즈를 이쪽 Lens에서 읽는다. 편집 모드에서는 Brain이 " +
             "Main Camera를 갱신하지 않으므로, 씬 뷰로 테스트할 때 이걸 지정하면 즉시 반영된다.")]
    [SerializeField] private CinemachineCamera zoomSourceCamera;

    [Tooltip("줌 범위(min/max)를 읽어올 컨트롤러. 비우면 씬에서 찾는다.")]
    [SerializeField] private CameraController2 zoomRangeSource;

    [Header("Mode")]
    [SerializeField] private Mode mode = Mode.NormalizedZoom;

    [Header("Normalized Zoom")]
    [Tooltip("켜면 해상도가 아니라 '블록이 화면에서 몇 px으로 보일지'로 지정한다.\n" +
             "resolution은 렌더 타깃 긴 변에 대한 상대값이라 창 크기가 바뀌면 룩이 달라진다 — " +
             "블록 px으로 지정하면 창 크기와 무관하게 같은 룩이 나온다.")]
    [SerializeField] private bool specifyByBlockPixels = true;

    [Tooltip("최대 확대에서 블록이 화면에서 차지할 픽셀 수")]
    [Min(1f)]
    [SerializeField] private float blockPixelsAtMinZoom = 4f;

    [Tooltip("최대 축소에서 블록이 화면에서 차지할 픽셀 수")]
    [Min(1f)]
    [SerializeField] private float blockPixelsAtMaxZoom = 2f;

    [Tooltip("최대 확대(orthoSize = min)에서의 해상도. specifyByBlockPixels가 꺼져 있을 때만 쓴다.")]
    [Min(1)]
    [SerializeField] private int resolutionAtMinZoom = 240;

    [Tooltip("최대 축소(orthoSize = max)에서의 해상도. specifyByBlockPixels가 꺼져 있을 때만 쓴다.")]
    [Min(1)]
    [SerializeField] private int resolutionAtMaxZoom = 480;

    [Tooltip("정규화된 줌(0=최대 확대, 1=최대 축소)을 보간 계수로 바꾸는 커브. 기본은 선형.")]
    [SerializeField] private AnimationCurve zoomCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("컨트롤러에서 못 읽거나 다른 범위로 시험할 때 켠다")]
    [SerializeField] private bool overrideZoomRange;

    [SerializeField] private float minOrthoSizeOverride = 20f;

    [SerializeField] private float maxOrthoSizeOverride = 100f;

    [Header("World Locked")]
    [Tooltip("블록 하나가 담는 월드 크기. 본진 기준 1.0이 형태 가독성의 하한이다(1.5면 뭉개진다).")]
    [Min(0.01f)]
    [SerializeField] private float blockWorldSize = 1.0f;

    [Header("Clamp")]
    [SerializeField] private int minResolution = 80;

    [SerializeField] private int maxResolution = 2000;

    [Header("Fixed Resolution")]
    [Min(1)]
    [SerializeField] private int fixedResolution = 480;

    [Header("Debug (읽기 전용)")]
    [SerializeField] private float currentOrthoSize;

    [SerializeField] private float currentZoom01;

    [SerializeField] private int currentResolution;

    [SerializeField] private float currentBlockScreenPixels;

    [SerializeField] private float currentBlockWorldSize;

    [Tooltip("resolution이 렌더 타깃 긴 변에 걸려 잘렸는지. true면 설정값이 과하다 — " +
             "긴 변을 넘는 resolution은 블록이 서브픽셀이 되어 픽셀레이션이 사실상 무효가 된다(실측 확인).")]
    [SerializeField] private bool clampedToScreen;

    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");

    private float _lastApplied = -1f;

    /// <summary>지금 적용된 resolution. 캡처 스크립트가 값을 확인할 때 쓴다.</summary>
    public int CurrentResolution => currentResolution;

    /// <summary>
    /// 즉시 재계산·적용한다. 편집 모드에서 스크린샷을 찍기 전에 호출한다 —
    /// LateUpdate가 언제 도는지에 기대지 않고 결정론적으로 값을 맞추기 위한 훅이다.
    /// </summary>
    public void Refresh()
    {
        Apply();
    }

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

        currentOrthoSize = ReadOrthoSize(cam);

        float height = Mathf.Max(1f, cam.pixelHeight);
        float longSide = Mathf.Max(cam.pixelWidth, cam.pixelHeight);

        int resolution = Mathf.Clamp(
            ResolveResolution(cam, longSide, height),
            Mathf.Max(1, minResolution),
            Mathf.Max(1, maxResolution));

        // 렌더 타깃 긴 변을 넘는 resolution은 블록이 서브픽셀이 되어 효과가 사라진다.
        // 실측: 849x478 게임 뷰에서 resolution 2400의 출력은 픽셀레이션 OFF와 고주파 에너지가
        // 소수점 4자리까지 동일했다(0.1201). 즉 무효값이므로 조용히 통과시키지 않고 잘라낸다.
        int screenCap = Mathf.Max(1, Mathf.FloorToInt(longSide));
        clampedToScreen = resolution > screenCap;

        if (clampedToScreen)
        {
            resolution = screenCap;
        }

        currentResolution = resolution;
        currentBlockScreenPixels = longSide / resolution;
        currentBlockWorldSize = 2f * currentOrthoSize * longSide / (resolution * height);

        float pixelSize = Mathf.Max(1f / resolution, 0.0001f);

        // 매 프레임 SetFloat을 때리지 않는다 — 서브에셋이라 불필요한 dirty를 만들 이유가 없다.
        if (Mathf.Approximately(pixelSize, _lastApplied))
        {
            return;
        }

        settings.effectMaterial.SetFloat(PixelSizeId, pixelSize);
        _lastApplied = pixelSize;
    }

    private int ResolveResolution(Camera cam, float longSide, float height)
    {
        if (mode == Mode.FixedResolution || !cam.orthographic)
        {
            // 퍼스펙티브 카메라는 orthographicSize가 의미 없으므로 고정값으로 폴백한다.
            return Mathf.Max(1, fixedResolution);
        }

        if (mode == Mode.WorldLocked)
        {
            // blockWorldSize = 2 · orthoSize · longSide / (resolution · height) 를 resolution에 대해 푼 것.
            // (검산: 1920x1080 · orthoSize 135 · blockWorldSize 1.0 → 480)
            return Mathf.RoundToInt(2f * currentOrthoSize * longSide / (height * blockWorldSize));
        }

        GetZoomRange(out float min, out float max);

        // 범위가 뒤집혔거나 0폭이면 보간이 무의미하다 — 최대 확대 쪽 값을 쓴다.
        if (max - min < 0.0001f)
        {
            return resolutionAtMinZoom;
        }

        currentZoom01 = Mathf.Clamp01((currentOrthoSize - min) / (max - min));
        float t = zoomCurve != null ? zoomCurve.Evaluate(currentZoom01) : currentZoom01;

        if (!specifyByBlockPixels)
        {
            return Mathf.RoundToInt(Mathf.LerpUnclamped(resolutionAtMinZoom, resolutionAtMaxZoom, t));
        }

        // 블록 px으로 지정하는 경로. resolution = 긴 변 / 블록 px 이므로 창 크기가 바뀌어도
        // 화면에서 보이는 블록 크기가 유지된다.
        float blockPixels = Mathf.Max(1f, Mathf.LerpUnclamped(blockPixelsAtMinZoom, blockPixelsAtMaxZoom, t));

        return Mathf.RoundToInt(longSide / blockPixels);
    }

    /// <summary>
    /// 현재 오쏘 사이즈. 편집 모드에서는 CinemachineBrain이 Main Camera를 갱신하지 않으므로,
    /// zoomSourceCamera가 지정돼 있으면 그쪽 Lens를 우선한다.
    /// </summary>
    private float ReadOrthoSize(Camera cam)
    {
        if (zoomSourceCamera != null && !Application.isPlaying)
        {
            return zoomSourceCamera.Lens.OrthographicSize;
        }

        return cam.orthographicSize;
    }

    private void GetZoomRange(out float min, out float max)
    {
        if (overrideZoomRange)
        {
            min = minOrthoSizeOverride;
            max = maxOrthoSizeOverride;
            return;
        }

        if (zoomRangeSource == null)
        {
            zoomRangeSource = FindAnyObjectByType<CameraController2>();
        }

        if (zoomRangeSource != null)
        {
            min = zoomRangeSource.MinZoomSize;
            max = zoomRangeSource.MaxZoomSize;
            return;
        }

        min = minOrthoSizeOverride;
        max = maxOrthoSizeOverride;
    }
}
