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

        Apply(dayPreset);
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
