using NorthLand.Core;
using UnityEngine;
using UnityEngine.InputSystem;

public class TitleSettingUI : MonoBehaviour
{
    [SerializeField] private GameObject settingPanel;

    [SerializeField]
    private LocalizationManager localizationManager;
    public bool IsOpen =>settingPanel != null &&settingPanel.activeSelf;

    private void Awake()
    {
        if (settingPanel == null)
        {
            Debug.LogError($"[{nameof(TitleSettingUI)}] Setting Panel이 연결되지 않았습니다.",this);

            enabled = false;
            return;
        }

        if (localizationManager == null)
        {
            Debug.LogError($"[{nameof(TitleSettingUI)}] localizationManager가 연결되지 않았습니다.", this);

            enabled = false;
            return;
        }

        settingPanel.SetActive(false);
    }

    private void Update()
    {
        if (Keyboard.current != null &&Keyboard.current.f1Key.wasPressedThisFrame)
        {
            TogglePanel();
        }
    }

    public void TogglePanel()
    {
        if (settingPanel.activeSelf)
        {

            localizationManager.OnClose();
            ClosePanel();
        }
        else
        {
            localizationManager.OnClose();
            OpenPanel();
        }
    }

    private void OpenPanel()
    {
        if (settingPanel == null)
            return;

        settingPanel.SetActive(true);
    }

    private void ClosePanel()
    {
        if (settingPanel == null)
            return;

        settingPanel.SetActive(false);

    }
    public void QuitGame()
    {
        if (GameSceneManager.Instance == null)
        {
            Debug.LogError("[ResultUIManager] GameSceneManager.Instance를 찾을 수 없습니다.");
            return;
        }

        GameSceneManager.Instance.QuitGame();
    }
}