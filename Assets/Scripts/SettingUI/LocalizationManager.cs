
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using static Unity.VisualScripting.Icons;

public class LocalizationManager : MonoBehaviour
{
    [SerializeField]
    private Button KoreaButton;
    [SerializeField]
    private TextMeshProUGUI KoreaText;

    [SerializeField]
    private Button USAButton;
    [SerializeField]
    private TextMeshProUGUI USAText;

    [SerializeField]
    private Button JapanButton;
    [SerializeField]
    private TextMeshProUGUI JapanText;


    [SerializeField]
    private GameObject LocalizationPanel;

    [SerializeField]
    private TextMeshProUGUI Language;




    private void Start()
    {
        KoreaButton.onClick.AddListener(() => ChangeLocale("ko-KR"));
        USAButton.onClick.AddListener(() => ChangeLocale("en-US"));
        JapanButton.onClick.AddListener(() => ChangeLocale("ja-JP"));
    }

    private void ChangeLocale(string code)
    {
        if (!LocalizationSettings.InitializationOperation.IsDone)
        {
            LocalizationSettings.InitializationOperation.Completed += _ => ApplyLocale(code);
            return;
        }

        switch (code){
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

        LocalizationPanel.SetActive(false);
        ApplyLocale(code);
    }

    private void ApplyLocale(string code)
    {
        var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
        if (locale == null)
        {
            Debug.LogWarning($"Locale not found: {code}");
            return;
        }

        LocalizationSettings.SelectedLocale = locale;
    }

    public void OnOpen()
    {
        LocalizationPanel.SetActive(true);
    }




}
