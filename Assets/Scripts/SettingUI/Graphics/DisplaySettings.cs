using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DisplaySettings : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown screenModeDropdown;
    [SerializeField] private TMP_Dropdown resolutionDropdown;

    private const string ScreenModeKey = "ScreenMode";
    private const string ResolutionIndexKey = "ResolutionIndex";
    private const string ResolutionWidthKey = "ResolutionWidth";
    private const string ResolutionHeightKey = "ResolutionHeight";


    [SerializeField] private GameObject resolutionConfirmPanel;
    [SerializeField] private TMP_Text resolutionConfirmText;
    [SerializeField] private Button keepResolutionButton;
    [SerializeField] private Button revertResolutionButton;

    private CancellationTokenSource resolutionConfirmCts;

    private int previousWidth;
    private int previousHeight;
    private int previousResolutionIndex;
    private int pendingResolutionIndex;
    private FullScreenMode previousScreenMode;


    private static readonly Vector2Int[] Resolutions =
    {
        new(1920, 1080),
        new(1600, 900),
        new(1280, 720)
    };

    private void Start()
    {
        if (resolutionDropdown != null)
        {
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
            int savedResolutionIndex = Mathf.Clamp(
                PlayerPrefs.GetInt(ResolutionIndexKey, 0),
                0,
                Resolutions.Length - 1);

            resolutionDropdown.SetValueWithoutNotify(savedResolutionIndex);
            resolutionDropdown.RefreshShownValue();

            Vector2Int savedResolution = Resolutions[savedResolutionIndex];
            SaveResolution(savedResolution.x, savedResolution.y);
        }
        else
        {
            Debug.LogWarning(
                "[DisplaySettings] Resolution Dropdown이 연결되지 않았습니다.",
                this);
        }

        int savedMode = PlayerPrefs.GetInt(ScreenModeKey, 1);

        screenModeDropdown.SetValueWithoutNotify(savedMode);
        ApplyScreenMode(savedMode);
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
        ApplyScreenMode(index);

        PlayerPrefs.SetInt(ScreenModeKey, index);
        PlayerPrefs.Save();
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
                resolutionConfirmText.text =$"이 화면 설정을 유지하시겠습니까?\n{remainingTime}초 후 이전 설정으로 돌아갑니다.";

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
        previousResolutionIndex = PlayerPrefs.GetInt(ResolutionIndexKey, 0);

        pendingResolutionIndex = index;

        Vector2Int selected = Resolutions[index];

        Screen.SetResolution(selected.x,selected.y,Screen.fullScreenMode);

        ShowResolutionConfirmation();
    }

    public void ConfirmResolutionChange()
    {
        CancelResolutionCountdown();

        Vector2Int selected =
            Resolutions[pendingResolutionIndex];

        PlayerPrefs.SetInt(ResolutionIndexKey,pendingResolutionIndex);

        SaveResolution(selected.x, selected.y);

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
        int width = PlayerPrefs.GetInt(ResolutionWidthKey, Screen.width);
        int height = PlayerPrefs.GetInt(ResolutionHeightKey, Screen.height);
        Screen.SetResolution(width, height, mode);
    }

    private static void SaveResolution(int width, int height)
    {
        PlayerPrefs.SetInt(ResolutionWidthKey, width);
        PlayerPrefs.SetInt(ResolutionHeightKey, height);
        PlayerPrefs.Save();
    }
}
