using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using NorthLand.Core;

public class DisplaySettings : MonoBehaviour
{
    private const string ResolutionConfirmationKey = "settings.graphics.resolution_confirmation";

    [SerializeField] private TMP_Dropdown screenModeDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;

    [SerializeField] private GameObject resolutionConfirmPanel;
    [SerializeField] private TMP_Text resolutionConfirmText;

    private CancellationTokenSource resolutionConfirmCts;

    private int previousWidth;
    private int previousHeight;
    private int previousResolutionIndex;
    private int pendingResolutionIndex;
    private FullScreenMode previousScreenMode;

    private static readonly Vector2Int[] Resolutions = GameSettingsConstraints.Resolutions;

    private void Awake()
    {
        if (Resolutions.Length !=
            GameSettingsConstraints.ResolutionOptionCount)
        {
            Debug.LogError($"[DisplaySettings] 해상도 옵션 개수가 설정 계약과 다릅니다.Expected: {GameSettingsConstraints.ResolutionOptionCount},Actual: {Resolutions.Length}",this);

            enabled = false;
        }
    }
    private void Start()
    {
        if (!enabled)
            return;

        if (GameSettingsService.Instance == null || GameSettingsService.Instance.CurrentSettings == null)
        {
            Debug.LogError("[DisplaySettings] GameSettingsService가 준비되지 않았습니다.",this);

            enabled = false;
            return;
        }

        GameSettingsData settings = GameSettingsService.Instance.CurrentSettings;

        int savedMode = Mathf.Clamp(settings.screenMode,GameSettingsConstraints.MinScreenModeIndex,GameSettingsConstraints.MaxScreenModeIndex);
        int savedResolutionIndex = Mathf.Clamp(settings.resolutionIndex,0,Resolutions.Length - 1);

        if (screenModeDropdown != null)
        {
            screenModeDropdown.SetValueWithoutNotify(savedMode);
            screenModeDropdown.RefreshShownValue();
        }

        if (resolutionDropdown != null)
        {
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

            resolutionDropdown.SetValueWithoutNotify(savedResolutionIndex);

            resolutionDropdown.RefreshShownValue();
        }
        else
        {
            Debug.LogWarning("[DisplaySettings] Resolution Dropdown이 연결되지 않았습니다.",this);
        }
    }
    private void OnDisable()
    {
        if (resolutionConfirmCts != null)
        {
            RevertResolutionChange();
        }
    }
    private void OnDestroy()
    {
        CancelResolutionCountdown();

        if (resolutionDropdown != null)
        {
            resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);
        }
    }

    public void OnScreenModeChanged(int index)
    {
        index = Mathf.Clamp(index,GameSettingsConstraints.MinScreenModeIndex,GameSettingsConstraints.MaxScreenModeIndex);

        ApplyScreenMode(index);

        GameSettingsData settings = GameSettingsService.Instance.CurrentSettings;

        GameSettingsService.Instance.SetDisplaySettings(index,settings.resolutionIndex);
    }

    private void ApplyScreenMode(int index)
    {
        switch (index)
        {
            case 0:
                ApplySavedResolution(FullScreenMode.ExclusiveFullScreen);
                break;

            case 1:
                Resolution nativeResolution = Screen.currentResolution;
                Screen.SetResolution(nativeResolution.width,nativeResolution.height,FullScreenMode.FullScreenWindow);
                break;

            case 2:
                ApplySavedResolution(FullScreenMode.Windowed);
                break;
        }
    }

    private void ShowResolutionConfirmation()
    {
        resolutionConfirmPanel.SetActive(true);
        resolutionDropdown.interactable = false;

        CancelResolutionCountdown();

        resolutionConfirmCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy()
            );

        ResolutionConfirmCountdownAsync(
            resolutionConfirmCts.Token
        ).Forget();
    }

    private async UniTask ResolutionConfirmCountdownAsync(
    CancellationToken cancellationToken)
    {
        try
        {
            for (int remainingTime = 15;remainingTime > 0;remainingTime--)
            {
                resolutionConfirmText.text = LocalizationHelper.Get(
                    LocalizationHelper.k_DefaultTable,
                    ResolutionConfirmationKey,
                    remainingTime);

                await UniTask.Delay(TimeSpan.FromSeconds(1),DelayType.UnscaledDeltaTime,PlayerLoopTiming.Update,cancellationToken);
            }

            RevertResolutionChange();
        }
        catch (OperationCanceledException)
        {
            // 사용자가 유지 또는 되돌리기를 눌러 취소된 정상적인 상황
        }
    }

    private void CancelResolutionCountdown()
    {
        if (resolutionConfirmCts == null)
            return;

        resolutionConfirmCts.Cancel();
        resolutionConfirmCts.Dispose();
        resolutionConfirmCts = null;
    }

    public void OnResolutionChanged(int index)
    {
        if (index < 0 || index >= Resolutions.Length)
            return;

        previousWidth = Screen.width;
        previousHeight = Screen.height;
        previousScreenMode = Screen.fullScreenMode;
        previousResolutionIndex = Mathf.Clamp(GameSettingsService.Instance.CurrentSettings.resolutionIndex,0,Resolutions.Length - 1);

        pendingResolutionIndex = index;

        Vector2Int selected = Resolutions[index];

        Screen.SetResolution(selected.x,selected.y,Screen.fullScreenMode);

        ShowResolutionConfirmation();
    }

    public void ConfirmResolutionChange()
    {
        CancelResolutionCountdown();

        GameSettingsData settings = GameSettingsService.Instance.CurrentSettings;

        GameSettingsService.Instance.SetDisplaySettings(settings.screenMode,pendingResolutionIndex);

        resolutionConfirmPanel.SetActive(false);
        resolutionDropdown.interactable = true;
    }

    public void RevertResolutionChange()
    {
        CancelResolutionCountdown();

        Screen.SetResolution(previousWidth,previousHeight,previousScreenMode);

        resolutionDropdown.SetValueWithoutNotify(previousResolutionIndex);

        resolutionDropdown.RefreshShownValue();
        resolutionConfirmPanel.SetActive(false);
        resolutionDropdown.interactable = true;
    }

    private void ApplySavedResolution(FullScreenMode mode)
    {
        int index = Mathf.Clamp(GameSettingsService.Instance.CurrentSettings.resolutionIndex,0,Resolutions.Length - 1);

        Vector2Int resolution = Resolutions[index];

        Screen.SetResolution(resolution.x,resolution.y,mode);
    }

}
