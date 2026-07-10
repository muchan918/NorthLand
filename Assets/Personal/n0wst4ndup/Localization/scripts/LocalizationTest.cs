using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;

public class LocalizationTest : MonoBehaviour
{
    [SerializeField] Button krBtn;
    [SerializeField] Button enBtn;
    [SerializeField] Button jpBtn;

    private void Start()
    {
        krBtn.onClick.AddListener(() => ChangeLocale("ko-KR"));
        enBtn.onClick.AddListener(() => ChangeLocale("en-US"));
        jpBtn.onClick.AddListener(() => ChangeLocale("ja-JP"));
    }

    private void ChangeLocale(string code)
    {
        LocalizationSettings.InitializationOperation.Completed += _ =>
        {
            var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
            if (locale == null)
            {
                Debug.LogWarning($"Locale not found: {code}");
                return;
            }

            LocalizationSettings.SelectedLocale = locale;
        };
    }
}