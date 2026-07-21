using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

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

    private UniTaskCompletionSource<WaveRewardData> selectionSource;

    private float previousTimeScale;

    private void Awake()
    {
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
        Time.timeScale = previousTimeScale;

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
        Time.timeScale = 0f;

        if (panel != null)
        {
            panel.SetActive(true);
        }

        if (openPanel != null)
        {
            openPanel.SetActive(false);
        }
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

        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

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
                panel.SetActive(false);
            }

            if (openPanel != null)
            {
                openPanel.SetActive(false);
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