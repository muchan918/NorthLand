using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    private GameObject Openpanel;

    private UniTaskCompletionSource<WaveRewardData> selectionSource;

    private void Awake()
    {
        if (panel != null)
        {
            panel.SetActive(false);
            Openpanel.SetActive(false);
        }
    }


    public void ClosePanel()
    {
        panel.SetActive(false);
        Openpanel.SetActive(true);
    }
    public void OpenPanel()
    {
        panel.SetActive(true);
        Openpanel.SetActive(false);
    }

    public async UniTask<WaveRewardData> SelectRewardAsync(IReadOnlyList<WaveRewardData> candidates,CancellationToken cancellationToken)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        selectionSource = new UniTaskCompletionSource<WaveRewardData>();

        ClearButtonListeners();
        ShowCandidates(candidates);

        if (panel != null)
        {
            panel.SetActive(true);
        }

        using CancellationTokenRegistration registration =cancellationToken.Register(() => selectionSource?.TrySetCanceled(cancellationToken));

        try
        {
            return await selectionSource.Task;
        }
        finally
        {
            ClearButtonListeners();

            if (panel != null)
            {
                panel.SetActive(false);
            }

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
                nameTexts[i].text = reward.DisplayName;
            }

            if (i < descriptionTexts.Length &&
                descriptionTexts[i] != null)
            {
                descriptionTexts[i].text = reward.Description;
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