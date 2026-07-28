using NorthLand.Core;
using UnityEngine;

public class SettingUI : MonoBehaviour
{
    [SerializeField]
    private GameObject settingPanel;

    [SerializeField]
    private LocalizationManager localizationManager;

    // 인게임에서만 연결한다. 타이틀에서는 비워 둔다.
    [SerializeField]
    private GameSpeedController gameSpeedController;

    public bool IsOpen =>
        settingPanel != null &&
        settingPanel.activeSelf;

    private void Awake()
    {
        if (settingPanel == null)
        {
            Debug.LogError($"[{nameof(SettingUI)}] SettingPanel이 연결되지 않았습니다.",this);

            enabled = false;
            return;
        }

        if (localizationManager == null)
        {
            Debug.LogError($"[{nameof(SettingUI)}] LocalizationManager가 연결되지 않았습니다.",this);

            enabled = false;
            return;
        }

        // GameSpeedController는 타이틀에 없으므로 필수 검사를 하지 않는다.
        localizationManager.OnClose();
        settingPanel.SetActive(false);
    }

    public void TogglePanel()
    {
        if (IsOpen)
        {
            ClosePanel();
        }
        else
        {
            OpenPanel();
        }
    }

    public void OpenPanel()
    {
        if (settingPanel == null)
            return;

        localizationManager?.OnClose();
        settingPanel.SetActive(true);

        gameSpeedController?.SetPaused(GamePauseReason.Settings,true);
    }

    public void ClosePanel()
    {
        if (settingPanel == null)
            return;

        localizationManager?.OnClose();
        settingPanel.SetActive(false);

        gameSpeedController?.SetPaused(GamePauseReason.Settings,false);
    }

    public void QuitGame()
    {
        if (GameSceneManager.Instance == null)
        {
            Debug.LogError($"[{nameof(SettingUI)}] GameSceneManager 인스턴스를 찾을 수 없습니다.",this);

            return;
        }

        GameSceneManager.Instance.QuitGame();
    }
}