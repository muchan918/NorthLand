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

        // 씬이 셀셰이딩(FlatKit)이라 라이트 강도를 내려도 화면이 거의 어두워지지 않는다(#136).
        // 셀 램프가 빛을 단계로 양자화해서, 빛이 0이 되기 전까지는 계속 "밝은 밴드"로 판정하기 때문.
        // 그래서 밤의 어둡기·색은 라이트가 아니라 NightVolume(ColorAdjustments)이 만든다.
        [Header("Post (NightVolume weight)")]
        [Range(0f, 1f)] public float nightVolumeWeight;

        // 물(WaterURP_Ortho)은 언릿이라 라이팅에 전혀 반응하지 않는다. 포스트프로세싱으로
        // 같이 어두워지긴 하지만 원래가 밝아서 주변보다 뜬다 — 이 틴트로 한 번 더 눌러준다.
        // 흰색이면 머티리얼에 authoring된 색 그대로(= 낮).
        [Header("Water tint (언릿이라 별도 보정)")]
        public Color waterTint = Color.white;
    }

    private static readonly int SkyboxExposureId = Shader.PropertyToID("_Exposure");
    private static readonly int SkyboxTintId = Shader.PropertyToID("_SkyTint");

    // WaterURP_Ortho가 노출한 색 프로퍼티 전부. 낮 값을 캐시해 두고 waterTint를 곱해 적용한다.
    private static readonly int[] WaterColorIds =
    {
        Shader.PropertyToID("_ColorSurface"),
        Shader.PropertyToID("_ColorShallow"),
        Shader.PropertyToID("_ColorDeep"),
        Shader.PropertyToID("_ColorAmbient"),
        Shader.PropertyToID("_ColorFoam")
    };

    [SerializeField] private Light directionalLight;

    [SerializeField]
    [Tooltip("밤 전용 포스트프로세싱 볼륨(NightLookProfile). weight를 0에서 1로 전환한다.")]
    private Volume nightVolume;

    [SerializeField]
    [Tooltip("물 렌더러. 비워 두면 물 틴트를 건너뛴다.")]
    private Renderer waterRenderer;

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
    private MaterialPropertyBlock _waterBlock;
    private Color[] _waterDayColors;

    [SerializeField]
    private LightingPreset dayPreset = new LightingPreset
    {
        lightIntensity = 1.5f,
        lightColor = new Color(1f, 0.957f, 0.839f),
        ambientSkyColor = new Color(0.3f, 0.32f, 0.38f),
        ambientEquatorColor = new Color(0.24f, 0.23f, 0.22f),
        ambientGroundColor = new Color(0.14f, 0.13f, 0.12f),
        skyboxExposure = 1.3f,
        skyboxTint = new Color(0.5f, 0.5f, 0.5f),
        nightVolumeWeight = 0f,
        waterTint = Color.white
    };

    // 동화풍 달빛(#136). 라이트는 형태와 전투 가독성을 위해 일부러 높게 두고,
    // 어둡기와 청보라 톤은 nightVolumeWeight(ColorAdjustments)가 만든다.
    // 측정 기준: 낮 대비 평균 휘도 약 49%, 화면 평균 RGB의 B/R 약 1.6(차가움).
    [SerializeField]
    private LightingPreset nightPreset = new LightingPreset
    {
        lightIntensity = 0.9f,
        lightColor = new Color(0.62f, 0.72f, 1f),
        ambientSkyColor = new Color(0.2f, 0.24f, 0.42f),
        ambientEquatorColor = new Color(0.15f, 0.17f, 0.3f),
        ambientGroundColor = new Color(0.08f, 0.09f, 0.16f),
        skyboxExposure = 0.55f,
        skyboxTint = new Color(0.14f, 0.17f, 0.34f),
        nightVolumeWeight = 1f,
        waterTint = new Color(0.42f, 0.52f, 0.78f)
    };

    private void Awake()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;

        CacheWaterDayColors();

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

        if (nightVolume != null) dayPreset.nightVolumeWeight = nightVolume.weight;

        // 물의 낮 색은 머티리얼이 단일 출처이므로, 틴트가 흰색인 것(= 그대로 쓴다)이 곧 낮이다.
        dayPreset.waterTint = Color.white;

        // 스카이박스는 런타임 사본을 만들기 **전에** 원본에서 읽는다. 프로퍼티가 없는 스카이박스
        // (Procedural이 아닌 큐브맵 등)면 씬 값을 알 수 없으므로 프리셋에 적힌 값을 그대로 둔다.
        Material sky = RenderSettings.skybox;

        if (sky != null)
        {
            if (sky.HasProperty(SkyboxExposureId)) dayPreset.skyboxExposure = sky.GetFloat(SkyboxExposureId);
            if (sky.HasProperty(SkyboxTintId)) dayPreset.skyboxTint = sky.GetColor(SkyboxTintId);
        }
    }

    /// <summary>
    /// 물 머티리얼에 authoring된 색을 낮 값으로 캐시한다. 이후 틴트는 이 값에 곱해서
    /// MaterialPropertyBlock으로 얹으므로 머티리얼 에셋 자체는 건드리지 않는다.
    /// </summary>
    private void CacheWaterDayColors()
    {
        if (waterRenderer == null) return;

        Material water = waterRenderer.sharedMaterial;

        if (water == null) return;

        _waterBlock = new MaterialPropertyBlock();
        _waterDayColors = new Color[WaterColorIds.Length];

        for (int i = 0; i < WaterColorIds.Length; i++)
        {
            _waterDayColors[i] = water.HasProperty(WaterColorIds[i])
                ? water.GetColor(WaterColorIds[i])
                : Color.white;
        }
    }

    private void ApplyWaterTint(Color tint)
    {
        if (waterRenderer == null || _waterBlock == null || _waterDayColors == null) return;

        waterRenderer.GetPropertyBlock(_waterBlock);

        for (int i = 0; i < WaterColorIds.Length; i++)
        {
            _waterBlock.SetColor(WaterColorIds[i], _waterDayColors[i] * tint);
        }

        waterRenderer.SetPropertyBlock(_waterBlock);
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

        if (nightVolume != null) nightVolume.weight = preset.nightVolumeWeight;

        ApplyWaterTint(preset.waterTint);
    }
}
