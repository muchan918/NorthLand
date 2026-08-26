using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using NorthLand.Core;

public class LocalizationManager : MonoBehaviour
{

    [SerializeField] private Button KoreaButton;
    [SerializeField] private TextMeshProUGUI KoreaText;

    [SerializeField] private Button USAButton;
    [SerializeField] private TextMeshProUGUI USAText;

    [SerializeField] private Button JapanButton;
    [SerializeField] private TextMeshProUGUI JapanText;

    [SerializeField] private GameObject LocalizationPanel;
    [SerializeField] private TextMeshProUGUI Language;

    private void Start()
    {
        KoreaButton.onClick.AddListener(() => ChangeLocale("ko-KR"));
        USAButton.onClick.AddListener(() => ChangeLocale("en-US"));
        JapanButton.onClick.AddListener(() => ChangeLocale("ja-JP"));

        LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;

        InitializeLocale();
    }

    private void InitializeLocale()
    {
        if (!LocalizationSettings.InitializationOperation.IsDone)
        {
            LocalizationSettings.InitializationOperation.Completed += _ =>
            {
                LoadSavedLocale();
            };

            return;
        }

        LoadSavedLocale();
    }

    private void LoadSavedLocale()
    {
        GameSettingsService service = GameSettingsService.Instance;

        if (service == null || service.CurrentSettings == null)
        {
            Debug.LogWarning("[LocalizationManager] 게임 설정이 준비되지 않았습니다.",this);

            ApplyLocale("ko-KR");
            return;
        }

        ApplyLocale(service.CurrentSettings.localeCode);
    }

    private void ChangeLocale(string code)
    {
        GameSettingsService service = GameSettingsService.Instance;

        if (service == null)
        {
            Debug.LogWarning("[LocalizationManager] 게임 설정 서비스를 찾을 수 없습니다.",this);

            return;
        }

        if (!service.TrySetLocale(code, out string error))
        {
            Debug.LogWarning($"언어 설정 저장에 실패했습니다: {error}",this);

            return;
        }

        ApplyLocale(code);
        LocalizationPanel.SetActive(false);
    }

    private void ApplyLocale(string code)
    {
        Locale locale =
            LocalizationSettings.AvailableLocales.GetLocale(code);

        if (locale == null)
        {
            Debug.LogWarning($"Locale not found: {code}");
            return;
        }

        LocalizationSettings.SelectedLocale = locale;
        UpdateLanguageText(code);
    }

    private void HandleLocaleChanged(Locale locale)
    {
        if (locale == null)
        {
            return;
        }

        UpdateLanguageText(locale.Identifier.Code);
    }

    private void UpdateLanguageText(string code)
    {
        switch (code)
        {
            case "ko-KR":
                Language.text = KoreaText.text;
                break;

            case "en-US":
                Language.text = USAText.text;
                break;

            case "ja-JP":
                Language.text = JapanText.text;
                break;
        }
    }

    private void OnDestroy()
    {
        LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;
    }

    public void OnOpen()
    {
        LocalizationPanel.SetActive(true);
    }

    public void OnClose()
    {
        LocalizationPanel.SetActive(false);
    }
}