using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

public class LocalizationManager : MonoBehaviour
{
    private const string LocalePreferenceKey = "SelectedLocale";

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
        string currentCode =
            LocalizationSettings.SelectedLocale?.Identifier.Code ?? "ko-KR";

        string savedCode =
            PlayerPrefs.GetString(LocalePreferenceKey, currentCode);

        ApplyLocale(savedCode);
    }

    private void ChangeLocale(string code)
    {
        PlayerPrefs.SetString(LocalePreferenceKey, code);
        PlayerPrefs.Save();

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