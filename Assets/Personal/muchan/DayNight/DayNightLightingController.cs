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
        RenderSettings.skybox = new Material(RenderSettings.skybox);
        RenderSettings.ambientMode = AmbientMode.Trilight;

        Apply(dayPreset);
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
        if (DayNightManager.Instance == null) return;
        DayNightManager.Instance.OnDayToNight -= HandleDayToNight;
        DayNightManager.Instance.OnNightToDay -= HandleNightToDay;
    }

    private void HandleDayToNight() => Apply(nightPreset);
    private void HandleNightToDay() => Apply(dayPreset);

    private void Apply(LightingPreset preset)
    {
        directionalLight.intensity = preset.lightIntensity;
        directionalLight.color = preset.lightColor;

        RenderSettings.ambientSkyColor = preset.ambientSkyColor;
        RenderSettings.ambientEquatorColor = preset.ambientEquatorColor;
        RenderSettings.ambientGroundColor = preset.ambientGroundColor;

        RenderSettings.skybox.SetFloat(SkyboxExposureId, preset.skyboxExposure);
        RenderSettings.skybox.SetColor(SkyboxTintId, preset.skyboxTint);
    }
}
