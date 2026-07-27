using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using UnityEngine.Localization.Components;

public class WaveRewardSelectionUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField]
    private GameObject panel;

    [Header("Reward Buttons")]
    [SerializeField]
    private Button[] rewardButtons;

    [SerializeField]
    private LocalizeStringEvent[] nameLocalizers;

    [SerializeField]
    private LocalizeStringEvent[] descriptionLocalizers;

    [SerializeField]
    private Image[] iconImages;

    [SerializeField]
    [FormerlySerializedAs("Openpanel")]
    private GameObject openPanel;

    public bool Camerastop => panel != null && panel.activeSelf;

    private UniTaskCompletionSource<WaveRewardData> selectionSource;

    [SerializeField]
    private GameSpeedController gameSpeedController;


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

        if (gameSpeedController != null)
        {
            gameSpeedController.SetPaused(GamePauseReason.Reward,true);
        }
        else
        {
            Debug.LogError(
                "[WaveRewardSelectionUI] GameSpeed가 연결되지 않았습니다.",
                this);
        }

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
            if (gameSpeedController != null)
            {
                gameSpeedController.SetPaused(GamePauseReason.Reward,false);
            }

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
            if (i < nameLocalizers.Length && nameLocalizers[i] != null)
            {
                nameLocalizers[i].StringReference.SetReference(LocalizationHelper.k_RewardsTable,reward.DisplayName);

                nameLocalizers[i].RefreshString();
            }

            if (i < descriptionLocalizers.Length && descriptionLocalizers[i] != null)
            {
                descriptionLocalizers[i].StringReference.SetReference(LocalizationHelper.k_RewardsTable,reward.Description);

                descriptionLocalizers[i].RefreshString();
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