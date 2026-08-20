using NorthLand.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class SettingUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField]
    private GameObject settingPanel;

    [SerializeField]
    private LocalizationManager localizationManager;

    [Header("Buttons")]
    [SerializeField]
    private Button settingbutton;

    [Header("Input")]
    [SerializeField] private bool toggleWithEscape = true;


    public bool IsOpen => settingPanel != null && settingPanel.activeSelf;

    private void Awake()
    {
        RegisterButtonEvents();

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

        localizationManager.OnClose();
        settingPanel.SetActive(false);
    }

    private void Update()
    {
        if (!toggleWithEscape)
            return;

        if (Keyboard.current == null)
            return;

        if (!Keyboard.current.escapeKey.wasPressedThisFrame)
            return;

        TogglePanel();
    }

    private void OnDestroy()
    {
        UnregisterButtonEvents();

        // 설정창이 열린 상태에서 오브젝트가 파괴될 경우
        // Settings 정지 사유가 남지 않도록 해제한다.
        if (GameSpeedController.Instance != null)
        {
            GameSpeedController.Instance.SetPaused(GamePauseReason.Settings,false);
        }
    }


    private void UnregisterButtonEvents()
    {
        if (settingbutton != null)
        {
            settingbutton.onClick.RemoveListener(TogglePanel);
        }

    }

    private void RegisterButtonEvents()
    {
        if (settingbutton != null)
        {
            settingbutton.onClick.AddListener(TogglePanel);
        }
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
        {
            return;
        }

        MouseManager.Instance?.CancelInteractions();

        if (localizationManager != null)
        {
            localizationManager.OnClose();
        }

        settingPanel.SetActive(true);

        if (GameSpeedController.Instance != null)
        {
            GameSpeedController.Instance.SetPaused(GamePauseReason.Settings,true);
        }

    }

    public void ClosePanel()
    {
        if (settingPanel == null)
        {
            return;
        }

        if (localizationManager != null)
        {
            localizationManager.OnClose();
        }

        settingPanel.SetActive(false);

        if (GameSpeedController.Instance != null)
        {
            GameSpeedController.Instance.SetPaused(GamePauseReason.Settings, false);
        }
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

    public void LoadMainMenu()
    {
        if (GameSceneManager.Instance == null)
        {
            Debug.LogError($"[{nameof(SettingUI)}] GameSceneManager 인스턴스를 찾을 수 없습니다.",this);

            return;
        }

        GameSceneManager.Instance.LoadMainMenu();
    }

    public void RetryGame()
    {
        if (GameSceneManager.Instance == null)
        {
            Debug.LogError($"[{nameof(SettingUI)}] GameSceneManager 인스턴스를 찾을 수 없습니다.",this);

            return;
        }

        GameSceneManager.Instance.LoadManageSpace();
    }
}
