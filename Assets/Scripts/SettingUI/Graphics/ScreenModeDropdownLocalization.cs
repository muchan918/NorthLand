using TMPro;
using UnityEngine;
using UnityEngine.Localization;

public class ScreenModeDropdownLocalization : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;

    [Header("Localized Strings")]
    [SerializeField] private LocalizedString fullscreen;
    [SerializeField] private LocalizedString borderless;
    [SerializeField] private LocalizedString windowed;

    private void OnEnable()
    {
        fullscreen.StringChanged += OnFullscreenChanged;
        borderless.StringChanged += OnBorderlessChanged;
        windowed.StringChanged += OnWindowedChanged;

        fullscreen.RefreshString();
        borderless.RefreshString();
        windowed.RefreshString();
    }

    private void OnDisable()
    {
        fullscreen.StringChanged -= OnFullscreenChanged;
        borderless.StringChanged -= OnBorderlessChanged;
        windowed.StringChanged -= OnWindowedChanged;
    }

    private void OnFullscreenChanged(string text)
    {
        SetOptionText(0, text);
    }

    private void OnBorderlessChanged(string text)
    {
        SetOptionText(1, text);
    }

    private void OnWindowedChanged(string text)
    {
        SetOptionText(2, text);
    }

    private void SetOptionText(int index, string text)
    {
        if (dropdown == null || index >= dropdown.options.Count)
            return;

        dropdown.options[index].text = text;
        dropdown.RefreshShownValue();
    }
}