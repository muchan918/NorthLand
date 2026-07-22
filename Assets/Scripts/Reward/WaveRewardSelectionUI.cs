using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

public class WaveRewardSelectionUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField]
    private GameObject panel;

    [Header("Reward Buttons")]
    [SerializeField]
    private Button[] rewardButtons;

    [SerializeField]
    private TMP_Text[] nameTexts;

    [SerializeField]
    private TMP_Text[] descriptionTexts;

    [SerializeField]
    private Image[] iconImages;

    [SerializeField]
    [FormerlySerializedAs("Openpanel")]
    private GameObject openPanel;

    public bool Camerastop=false;

    private UniTaskCompletionSource<WaveRewardData> selectionSource;

    private float previousTimeScale;

    private IReadOnlyList<WaveRewardData> currentCandidates;

    private void Awake()
    {

        Camerastop = false;
        if (panel != null)
        {
            panel.SetActive(false);
        }

        if (openPanel != null)
        {
            openPanel.SetActive(false);
        }
    }


    public void ClosePanel()
    {
        Camerastop = false;
        if (panel != null)
        {
            panel.SetActive(false);
        }

        if (openPanel != null)
        {
            openPanel.SetActive(true);
        }
    }
    public void OpenPanel()
    {
        Camerastop = true;
        if (panel != null)
        {
            panel.SetActive(true);
        }

        if (openPanel != null)
        {
            openPanel.SetActive(false);
        }
    }

    private void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    private void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    private void OnLocaleChanged(Locale locale)
    {
        RefreshLocalizedTexts();
    }
    private void RefreshLocalizedTexts()
    {
        if (currentCandidates == null)
        {
            return;
        }

        for (int i = 0; i < currentCandidates.Count && i < nameTexts.Length; i++)
        {
            WaveRewardData reward = currentCandidates[i];

            if (reward == null || nameTexts[i] == null)
            {
                continue;
            }

            nameTexts[i].text = LocalizationHelper.Get(LocalizationHelper.k_RewardsTable,reward.DisplayName);
            descriptionTexts[i].text = LocalizationHelper.Get(LocalizationHelper.k_RewardsTable, reward.Description);
        }
    }

    public async UniTask<WaveRewardData> SelectRewardAsync(IReadOnlyList<WaveRewardData> candidates,CancellationToken cancellationToken)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        selectionSource = new UniTaskCompletionSource<WaveRewardData>();
        currentCandidates = candidates;

        ClearButtonListeners();
        ShowCandidates(candidates);

        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        Camerastop = true;

        if (panel != null)
        {
            panel.SetActive(true);
        }

        if (openPanel != null)
        {
            openPanel.SetActive(false);
        }

        using CancellationTokenRegistration registration =cancellationToken.Register(() => selectionSource?.TrySetCanceled(cancellationToken));

        try
        {
            return await selectionSource.Task;
        }
        finally
        {
            ClearButtonListeners();
            Time.timeScale = previousTimeScale;

            if (panel != null)
            {
                Camerastop = false;
                panel.SetActive(false);
            }

            if (openPanel != null)
            {
                Camerastop = false;
                openPanel.SetActive(false);
            }

            currentCandidates = null;
            selectionSource = null;
        }
    }

    private void ShowCandidates(IReadOnlyList<WaveRewardData> candidates)
    {
        for (int i = 0; i < rewardButtons.Length; i++)
        {
            bool hasCandidate = i < candidates.Count && candidates[i] != null;

            rewardButtons[i].gameObject.SetActive(hasCandidate);

            if (!hasCandidate)
            {
                continue;
            }

            WaveRewardData reward = candidates[i];

            if (i < nameTexts.Length && nameTexts[i] != null)
            {
                nameTexts[i].text = LocalizationHelper.Get(LocalizationHelper.k_RewardsTable,reward.DisplayName);
            }

            if (i < descriptionTexts.Length &&
                descriptionTexts[i] != null)
            {
                descriptionTexts[i].text = LocalizationHelper.Get(LocalizationHelper.k_RewardsTable, reward.Description);
            }

            if (i < iconImages.Length &&
                iconImages[i] != null)
            {
                iconImages[i].sprite = reward.Icon;
                iconImages[i].enabled = reward.Icon != null;
            }

            rewardButtons[i].onClick.AddListener(() => SelectReward(reward)
            );
        }
    }

    private void SelectReward(WaveRewardData reward)
    {
        selectionSource?.TrySetResult(reward);
    }

    private void ClearButtonListeners()
    {
        if (rewardButtons == null)
        {
            return;
        }

        foreach (Button button in rewardButtons)
        {
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
            }
        }
    }
}