using UnityEngine;
using UnityEngine.Rendering;

public class DayNightLightingController : MonoBehaviour
{
    [System.Serializable]
    private class LightingPreset
    {
        [Header("Directional Light")]
        public float lightIntensity = 1f;
        public Color lightColor = Color.white;

        [Header("Ambient (Trilight)")]
        public Color ambientSkyColor = Color.white;
        public Color ambientEquatorColor = Color.gray;
        public Color ambientGroundColor = Color.gray;

        [Header("Skybox (Procedural)")]
        [Range(0f, 8f)] public float skyboxExposure = 1.3f;
        public Color skyboxTint = Color.gray;
    }

    private static readonly int SkyboxExposureId = Shader.PropertyToID("_Exposure");
    private static readonly int SkyboxTintId = Shader.PropertyToID("_SkyTint");

    [SerializeField] private Light directionalLight;

    // 낮 값의 단일 출처를 씬으로 만드는 스위치(#148).
    //
    // 왜 필요한가: Awake가 조건 없이 Apply(dayPreset)를 돌려 라이트·앰비언트·스카이박스를 덮으므로,
    // 씬에 authoring한 낮 룩이 플레이 진입 순간 무효가 된다. 그래서 전역 비주얼 룩(#148)을 맞추려면
    // 같은 값을 씬과 dayPreset 양쪽에 넣어야 했고, 한쪽만 고치면 편집 모드와 플레이 모드 룩이 갈렸다.
    //
    // 이 스위치가 켜져 있으면 Awake에서 dayPreset을 **쓰지 않고 현재 씬 값을 읽어 채운다.**
    // → 씬이 단일 출처가 되고, nightPreset은 그대로 밤 전용 값으로 남는다(밤은 씬에 authoring할
    //   대상이 아니라 전환 목표값이므로 여전히 프리셋이 맞다).
    // 끄면 종전대로 dayPreset을 씬에 적용한다(프리셋으로 낮 룩을 몰고 싶은 씬용).
    [SerializeField]
    [Tooltip("켜면 낮 프리셋을 씬 현재 값(라이트·앰비언트·스카이박스)에서 읽어 채운다. " +
             "씬이 낮 룩의 단일 출처가 되어 편집 모드와 플레이 모드가 갈리지 않는다.")]
    private bool captureDayPresetFromScene = true;

    private Material _runtimeSkybox;

    [SerializeField]
    private LightingPreset dayPreset = new LightingPreset
    {
        lightIntensity = 1.2f,
        lightColor = new Color(1f, 0.957f, 0.839f),
        ambientSkyColor = new Color(0.6f, 0.65f, 0.75f),
        ambientEquatorColor = new Color(0.45f, 0.45f, 0.4f),
        ambientGroundColor = new Color(0.3f, 0.28f, 0.25f),
        skyboxExposure = 1.3f,
        skyboxTint = new Color(0.5f, 0.5f, 0.5f)
    };

    [SerializeField]
    private LightingPreset nightPreset = new LightingPreset
    {
        lightIntensity = 0.4f,
        lightColor = new Color(0.55f, 0.6f, 0.85f),
        ambientSkyColor = new Color(0.18f, 0.2f, 0.28f),
        ambientEquatorColor = new Color(0.14f, 0.14f, 0.2f),
        ambientGroundColor = new Color(0.08f, 0.08f, 0.1f),
        skyboxExposure = 0.55f,
        skyboxTint = new Color(0.2f, 0.2f, 0.28f)
    };

    private void Awake()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;

        if (captureDayPresetFromScene)
        {
            // 씬 값을 프리셋으로 흡수만 하고 아무것도 덮지 않는다 — 지금 화면이 곧 낮 룩이다.
            CaptureDayPresetFromScene();
            return;
        }

        Apply(dayPreset);
    }

    /// <summary>
    /// 현재 씬의 라이트·앰비언트·스카이박스 값을 dayPreset에 담는다. 낮으로 돌아올 때
    /// (HandleNightToDay) 이 값으로 복원되므로, 씬을 고치면 그것이 그대로 낮 룩이 된다.
    /// </summary>
    private void CaptureDayPresetFromScene()
    {
        if (directionalLight != null)
        {
            dayPreset.lightIntensity = directionalLight.intensity;
            dayPreset.lightColor = directionalLight.color;
        }

        dayPreset.ambientSkyColor = RenderSettings.ambientSkyColor;
        dayPreset.ambientEquatorColor = RenderSettings.ambientEquatorColor;
        dayPreset.ambientGroundColor = RenderSettings.ambientGroundColor;

        // 스카이박스는 런타임 사본을 만들기 **전에** 원본에서 읽는다. 프로퍼티가 없는 스카이박스
        // (Procedural이 아닌 큐브맵 등)면 씬 값을 알 수 없으므로 프리셋에 적힌 값을 그대로 둔다.
        Material sky = RenderSettings.skybox;

        if (sky != null)
        {
            if (sky.HasProperty(SkyboxExposureId)) dayPreset.skyboxExposure = sky.GetFloat(SkyboxExposureId);
            if (sky.HasProperty(SkyboxTintId)) dayPreset.skyboxTint = sky.GetColor(SkyboxTintId);
        }
    }

    private void EnsureRuntimeSkybox()
    {
        if (_runtimeSkybox == null)
        {
            _runtimeSkybox = new Material(RenderSettings.skybox);
        }

        RenderSettings.skybox = _runtimeSkybox;
    }

    [ContextMenu("Preview Day Preset")]
    private void PreviewDayPreset() => Apply(dayPreset);

    [ContextMenu("Preview Night Preset")]
    private void PreviewNightPreset() => Apply(nightPreset);

    private void Start()
    {
        if (DayNightManager.Instance == null)
        {
            Debug.LogError("DayNightManager 없음");
            return;
        }

        DayNightManager.Instance.OnDayToNight += HandleDayToNight;
        DayNightManager.Instance.OnNightToDay += HandleNightToDay;
    }

    private void OnDestroy()
    {
        if (DayNightManager.Instance != null)
        {
            DayNightManager.Instance.OnDayToNight -= HandleDayToNight;
            DayNightManager.Instance.OnNightToDay -= HandleNightToDay;
        }

        if (_runtimeSkybox == null) return;

        if (Application.isPlaying) Destroy(_runtimeSkybox);
        else DestroyImmediate(_runtimeSkybox);
    }

    private void HandleDayToNight() => Apply(nightPreset);
    private void HandleNightToDay() => Apply(dayPreset);

    private void Apply(LightingPreset preset)
    {
        if (directionalLight == null)
        {
            Debug.LogError("Directional Light 미할당");
            return;
        }

        EnsureRuntimeSkybox();

        directionalLight.intensity = preset.lightIntensity;
        directionalLight.color = preset.lightColor;

        RenderSettings.ambientSkyColor = preset.ambientSkyColor;
        RenderSettings.ambientEquatorColor = preset.ambientEquatorColor;
        RenderSettings.ambientGroundColor = preset.ambientGroundColor;

        RenderSettings.skybox.SetFloat(SkyboxExposureId, preset.skyboxExposure);
        RenderSettings.skybox.SetColor(SkyboxTintId, preset.skyboxTint);
    }
}
