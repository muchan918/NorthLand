using UnityEngine;
using UnityEngine.InputSystem;

public class SettingUI : MonoBehaviour
{
    [SerializeField] private GameObject settingPanel;

    [SerializeField]
    private GameSpeedController gameSpeedController;

    public bool IsOpen =>settingPanel != null &&settingPanel.activeSelf;

    private void Awake()
    {
        if (settingPanel == null)
        {
            Debug.LogError("SettingPanel이 연결되지 않았습니다.", this);
            enabled = false;
            return;
        }

        settingPanel.SetActive(false);
    }

    private void Update()
    {
        if (Keyboard.current != null &&Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePanel();
        }
    }

    public void TogglePanel()
    {
        if (settingPanel.activeSelf)
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
        settingPanel.SetActive(true);

        gameSpeedController.SetPaused(GamePauseReason.Settings,true);
    }

    public void ClosePanel()
    {
        settingPanel.SetActive(false);

        gameSpeedController.SetPaused(GamePauseReason.Settings,false);
    }
}