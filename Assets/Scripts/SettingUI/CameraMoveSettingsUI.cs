using NorthLand.Core;
using UnityEngine;
using UnityEngine.UI;

public sealed class CameraMoveSettingsUI : MonoBehaviour
{
    [SerializeField] private Slider keyboardSpeedSlider;
    [SerializeField] private Slider mouseSpeedSlider;

    private void Awake()
    {
        keyboardSpeedSlider.minValue = 0.5f;
        keyboardSpeedSlider.maxValue = 2f;

        mouseSpeedSlider.minValue = 0.5f;
        mouseSpeedSlider.maxValue = 2f;
    }

    private void OnEnable()
    {
        LoadCurrentValues();

        keyboardSpeedSlider.onValueChanged.AddListener(HandleKeyboardSpeedChanged);

        mouseSpeedSlider.onValueChanged.AddListener(HandleMouseSpeedChanged);
    }

    private void OnDisable()
    {
        keyboardSpeedSlider.onValueChanged.RemoveListener(HandleKeyboardSpeedChanged);

        mouseSpeedSlider.onValueChanged.RemoveListener(HandleMouseSpeedChanged);
    }

    private void LoadCurrentValues()
    {
        GameSettingsService service = GameSettingsService.Instance;

        float keyboardValue = 1f;
        float mouseValue = 1f;

        if (service != null && service.CurrentSettings != null)
        {
            keyboardValue =
                service.CurrentSettings.keyboardMoveSpeedMultiplier;

            mouseValue =
                service.CurrentSettings.mouseMoveSpeedMultiplier;
        }

        keyboardSpeedSlider.SetValueWithoutNotify(keyboardValue);
        mouseSpeedSlider.SetValueWithoutNotify(mouseValue);
    }

    private void HandleKeyboardSpeedChanged(float value)
    {
        SaveCurrentValues();
    }

    private void HandleMouseSpeedChanged(float value)
    {
        SaveCurrentValues();
    }

    private void SaveCurrentValues()
    {
        GameSettingsService service = GameSettingsService.Instance;

        if (service == null)
        {
            return;
        }

        if (!service.TrySetCameraMoveSpeed(
                keyboardSpeedSlider.value,
                mouseSpeedSlider.value,
                out string error))
        {
            Debug.LogWarning(
                $"카메라 이동 속도 저장 실패: {error}",
                this);
        }
    }
}