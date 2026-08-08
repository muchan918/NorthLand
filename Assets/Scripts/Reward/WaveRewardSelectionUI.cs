using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Serialization;

public class WaveRewardSelectionUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField]
    private GameObject panel;

    [Header("Cards")]
    // 카드 한 장의 프리팹. 후보 수만큼 cardContainer 아래에 생성한다(#320).
    // 도입 전에는 씬에 Reward1~3을 고정 배치하고 요소별 평행 배열 6개로 인덱싱했다 —
    // 배열 순서가 어긋나면 조용히 엉뚱한 카드에 값이 들어갔고, 카드 개수도 씬에 박혀 있었다.
    [SerializeField]
    private RewardCardView cardPrefab;

    // 카드가 생성될 부모. HorizontalLayoutGroup을 붙여 두면 후보가 3장 미만일 때(#292 만렙 제외)
    // 빈 슬롯 없이 가운데로 모인다.
    [SerializeField]
    private Transform cardContainer;

    [Header("등급 색 (인덱스 0 = Lv1)")]
    // 카드면 틴트. 비워두면 아래 기본 팔레트를 쓰므로 인스펙터 배선 없이도 동작한다
    // (필드 초기화자는 이미 씬에 직렬화된 컴포넌트엔 적용되지 않아서, 폴백을 코드에 둔다).
    [SerializeField]
    private Color[] levelColors;

    // 동 → 은 → 금. 픽셀아트 톤에 맞춰 채도를 낮게 잡았다.
    private static readonly Color[] DefaultLevelColors =
    {
        new Color(0.69f, 0.55f, 0.34f), // Lv1
        new Color(0.78f, 0.82f, 0.85f), // Lv2
        new Color(0.95f, 0.76f, 0.31f), // Lv3
    };

    [SerializeField]
    [FormerlySerializedAs("Openpanel")]
    private GameObject openPanel;

    public bool Camerastop => panel != null && panel.activeSelf;

    private UniTaskCompletionSource<WaveRewardData> selectionSource;

    [SerializeField]
    private GameSpeedController gameSpeedController;

    private readonly List<RewardCardView> spawnedCards = new List<RewardCardView>();

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
            ClearCards();

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
        ClearCards();

        if (cardPrefab == null || cardContainer == null)
        {
            Debug.LogError(
                "[WaveRewardSelectionUI] 카드 프리팹 또는 컨테이너가 연결되지 않았습니다.",
                this);
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            WaveRewardData reward = candidates[i];

            if (reward == null)
            {
                continue;
            }

            RewardCardView card = Instantiate(cardPrefab, cardContainer);
            card.Bind(reward, ResolveTint(reward), SelectReward);
            spawnedCards.Add(card);
        }
    }

    // 이 보상을 고르면 도달할 레벨에 대응하는 카드 색. 레벨 표시(별)와 같은 값을 쓰도록
    // RewardCardView가 아니라 여기서 뽑아 넘긴다 — 매핑 소유자를 한 곳으로 모으기 위함.
    private Color ResolveTint(WaveRewardData reward)
    {
        Color[] palette = levelColors != null && levelColors.Length > 0
            ? levelColors
            : DefaultLevelColors;

        int nextLevel = 0;

        if (SkillEffectManager.Instance != null)
        {
            nextLevel = SkillEffectManager.Instance.GetNextLevel(reward.RewardType);
        }

        // 매니저가 없으면 nextLevel이 0이라 -1이 되는데 Clamp가 0으로 받아낸다(배열 밖 접근 방지).
        return palette[Mathf.Clamp(nextLevel - 1, 0, palette.Length - 1)];
    }

    private void ClearCards()
    {
        for (int i = 0; i < spawnedCards.Count; i++)
        {
            if (spawnedCards[i] != null)
            {
                Destroy(spawnedCards[i].gameObject);
            }
        }

        spawnedCards.Clear();
    }

    private void SelectReward(WaveRewardData reward)
    {
        selectionSource?.TrySetResult(reward);
    }
}
