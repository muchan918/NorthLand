using FlatKit;
using Unity.Cinemachine;
using UnityEngine;

// #148 픽셀레이션 해상도를 오쏘 줌에 연동한다(Docs/Rendering/VisualLookPipeline.md §3.7).
//
// 왜 필요한가: FlatKit Pixelation의 resolution은 "화면 긴 변의 픽셀 수"라 블록 크기가 화면 기준으로
// 고정된다. 오쏘 카메라를 줌 아웃하면 같은 블록이 더 넓은 월드를 담아 형태가 뭉개진다.
// 고정값으로는 게임 줌 범위에서 룩을 평가할 수 없으므로, 픽셀 채택 판단의 선행 조건이다.
//
// 왜 줌 범위로 정규화하는가: 줌 범위(CameraController2의 min/max)는 카메라 튜닝 중에 계속 바뀐다.
// 절대 기준으로 잡아두면 범위를 건드릴 때마다 값을 다시 유도해야 한다. 양 끝을 "최대 확대에서 이 해상도 /
// 최대 축소에서 이 해상도"로 정의하고 범위는 컨트롤러에서 직접 읽으므로, 범위가 바뀌어도 설정이 유효하다.
//
// 왜 settings.resolution을 쓰지 않고 머티리얼에 직접 쓰는가: FlatKit의 SetMaterialProperties()가
// private이고 Create()/에디터 OnValidate에서만 호출된다. 런타임에 settings.resolution만 바꿔도
// 반영되지 않는다. 그래서 노출된 settings.effectMaterial에 _PixelSize를 직접 쓴다(벤더 무수정).
//
// ⚠️ resolution은 렌더 타깃 긴 변에 대한 상대값이다. 긴 변 이상으로 잡으면 블록이 1px 이하가 되어
// 픽셀레이션이 꺼진 것과 동일해진다 — 849x478 게임 뷰에서 resolution 2400의 출력이 OFF와
// 고주파 에너지 소수점 4자리까지 같았다(실측). 작은 창에서 룩을 판정하지 말 것.
//
// ⚠️ effectMaterial은 PixelationSettings 에셋의 서브에셋이다. 이 컴포넌트가 값을 몰면
// 그 에셋의 _PixelSize가 git diff에 뜬다 — 매 프레임 덮어쓰는 값이라 무해하다.
[ExecuteAlways]
[AddComponentMenu("NorthLand/Rendering/Pixelation Zoom Binder")]
public class PixelationZoomBinder : MonoBehaviour
{
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

    [Header("Resolution")]
    [Tooltip("최대 확대(orthoSize = MinZoomSize)에서의 해상도")]
    [Min(1)]
    [SerializeField] private int resolutionAtMinZoom = 800;

    [Tooltip("최대 축소(orthoSize = MaxZoomSize)에서의 해상도")]
    [Min(1)]
    [SerializeField] private int resolutionAtMaxZoom = 2400;

    [Tooltip("정규화된 줌(0=최대 확대, 1=최대 축소)을 보간 계수로 바꾸는 커브. 기본은 선형.")]
    [SerializeField] private AnimationCurve zoomCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");

    private float _lastApplied = -1f;

    private void LateUpdate()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    /// <summary>
    /// 즉시 재계산·적용한다. 편집 모드에서 스크린샷을 찍기 전에 호출한다 —
    /// LateUpdate가 언제 도는지에 기대지 않고 결정론적으로 값을 맞추기 위한 훅이다.
    /// </summary>
    public void Refresh()
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

        if (cam == null || !cam.orthographic)
        {
            return;
        }

        if (!TryGetZoomRange(out float min, out float max))
        {
            return;
        }

        float zoom01 = Mathf.Clamp01((ReadOrthoSize(cam) - min) / (max - min));
        float t = zoomCurve != null ? zoomCurve.Evaluate(zoom01) : zoom01;

        int resolution = Mathf.Max(1, Mathf.RoundToInt(Mathf.LerpUnclamped(resolutionAtMinZoom, resolutionAtMaxZoom, t)));
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

    private bool TryGetZoomRange(out float min, out float max)
    {
        if (zoomRangeSource == null)
        {
            zoomRangeSource = FindAnyObjectByType<CameraController2>();
        }

        min = 0f;
        max = 0f;

        if (zoomRangeSource == null)
        {
            return false;
        }

        min = zoomRangeSource.MinZoomSize;
        max = zoomRangeSource.MaxZoomSize;

        // 범위가 뒤집혔거나 폭이 0이면 보간이 성립하지 않는다.
        return max - min > 0.0001f;
    }
}
