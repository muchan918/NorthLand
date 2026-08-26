using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public class BrightnessSettings : MonoBehaviour
{
    [SerializeField] private Slider brightnessSlider;
    [SerializeField, Range(0.1f, 10f)] private float exposureRange = 10f;

    private const string BrightnessKey = "Brightness";
    private const float DefaultBrightness = 0.5f;

    private readonly List<ExposureTarget> exposureTargets = new();

    private sealed class ExposureTarget
    {
        public ColorAdjustments Adjustments;
        public float BaseExposure;
    }

    private void Awake()
    {
        if (brightnessSlider == null)
        {
            Debug.LogWarning("[BrightnessSettings] Brightness Slider가 연결되지 않았습니다.", this);
            enabled = false;
            return;
        }

        brightnessSlider.minValue = 0f;
        brightnessSlider.maxValue = 1f;
        brightnessSlider.wholeNumbers = false;

        CacheRuntimeVolumeProfiles();

        float savedBrightness = Mathf.Clamp01(
            PlayerPrefs.GetFloat(BrightnessKey, DefaultBrightness));

        brightnessSlider.SetValueWithoutNotify(savedBrightness);
        ApplyBrightness(savedBrightness);
    }

    private void OnEnable()
    {
        if (brightnessSlider != null)
            brightnessSlider.onValueChanged.AddListener(OnBrightnessChanged);
    }

    private void OnDisable()
    {
        if (brightnessSlider != null)
            brightnessSlider.onValueChanged.RemoveListener(OnBrightnessChanged);
    }

    private void CacheRuntimeVolumeProfiles()
    {
        exposureTargets.Clear();

        Volume[] volumes = FindObjectsByType<Volume>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (Volume volume in volumes)
        {
            if (volume.sharedProfile == null)
                continue;

            // volume.profile은 sharedProfile을 복제하므로 원본 에셋이 변경되지 않는다.
            VolumeProfile runtimeProfile = volume.profile;

            if (!runtimeProfile.TryGet(out ColorAdjustments adjustments))
                adjustments = runtimeProfile.Add<ColorAdjustments>(true);

            adjustments.postExposure.overrideState = true;
            exposureTargets.Add(new ExposureTarget
            {
                Adjustments = adjustments,
                BaseExposure = adjustments.postExposure.value
            });
        }
    }

    public void OnBrightnessChanged(float value)
    {
        float clampedValue = Mathf.Clamp01(value);
        ApplyBrightness(clampedValue);

        PlayerPrefs.SetFloat(BrightnessKey, clampedValue);
        PlayerPrefs.Save();
    }

    private void ApplyBrightness(float value)
    {
        float exposureOffset = Mathf.Lerp(-exposureRange, exposureRange, value);

        foreach (ExposureTarget target in exposureTargets)
        {
            if (target.Adjustments != null)
                target.Adjustments.postExposure.value =
                    target.BaseExposure + exposureOffset;
        }
    }
}
