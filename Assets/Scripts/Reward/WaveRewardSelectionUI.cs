using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
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

    // 현재 카드로 떠 있는 후보. 로케일이 바뀔 때 이 목록으로 카드를 다시 그린다.
    private IReadOnlyList<WaveRewardData> boundCandidates;

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

        // 카드 텍스트는 LocalizationHelper.Get으로 한 번 당겨오는 방식이라 로케일 변경을 스스로
        // 따라가지 못한다. 보상 모달은 플레이어가 고를 때까지 떠 있는 지속형이고 그 위에
        // 설정 창이 열릴 수 있으므로, 컨테이너가 한 번만 구독해 카드를 일괄 재생성한다
        // (카드마다 구독하면 해제 누락이 카드 수만큼 늘어난다).
        LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;
    }

    private void OnDestroy()
    {
        LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;
    }

    private void HandleLocaleChanged(Locale locale)
    {
        if (boundCandidates == null || spawnedCards.Count == 0)
        {
            return;
        }

        ShowCandidates(boundCandidates);
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

        // 카드가 한 장도 안 만들어졌으면 흐름을 그대로 흘려보낸다. selectionSource는 카드 클릭으로만
        // 완료되므로, 여기서 막지 않으면 timeScale 0인 채로 빈 패널이 떠서 밤이 영원히 끝나지 않는다
        // (WaveCompletionCoordinator의 EndNight가 이 await 뒤에 있다).
        // pause와 패널 활성화보다 앞에서 나가야 화면에 흔적이 남지 않는다.
        if (!ShowCandidates(candidates))
        {
            ClearCards();
            selectionSource = null;

            return null;
        }

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

    // 카드를 한 장 이상 띄웠으면 true. 실패를 반환값으로 알리는 이유는 호출부 주석 참조.
    private bool ShowCandidates(IReadOnlyList<WaveRewardData> candidates)
    {
        ClearCards();

        boundCandidates = candidates;

        if (cardPrefab == null || cardContainer == null)
        {
            Debug.LogError(
                "[WaveRewardSelectionUI] 카드 프리팹 또는 컨테이너가 연결되지 않았습니다.",
                this);
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            WaveRewardData reward = candidates[i];

            if (reward == null)
            {
                continue;
            }

            // 레벨·수치를 카드당 한 번만 뜬다(#353) — 카드면 색과 카드 안 표시가 같은 값을 보게
            // 하려면 조회가 한 곳이어야 한다. 매니저가 없으면 default가 그대로 "효과 없음"이라
            // 별도 분기 없이 빈 카드 경로로 흐른다.
            SkillEffectSnapshot snapshot = SkillEffectManager.Instance != null
                ? SkillEffectManager.Instance.GetSnapshot(reward.RewardType)
                : default;

            RewardCardView card = Instantiate(cardPrefab, cardContainer);
            card.Bind(reward, snapshot, ResolveTint(snapshot), SelectReward);
            spawnedCards.Add(card);
        }

        return spawnedCards.Count > 0;
    }

    // 이 보상을 고르면 도달할 레벨에 대응하는 카드 색. 레벨→색 매핑 소유자를 한 곳에 모으려고
    // RewardCardView가 아니라 여기서 뽑아 넘긴다.
    //
    // 카드는 두 가지를 동시에 말한다(#353) — "지금 보유"는 채워진 별과 레벨 줄 왼쪽이, "고르면
    // 도달"은 미리보기 별과 이 색과 레벨 줄 오른쪽이 맡는다. 즉 이 색은 채워진 별이 아니라
    // 미리보기 별과 같은 편에 선다.
    //
    // 채워진 별에 맞춰 Level로 내리지 말 것: 만렙 효과는 후보에서 빠지므로(#292) 카드에
    // 뜨는 현재 레벨은 0~2뿐이고, palette[level-1]은 Lv0과 Lv1이 같은 동색이 되며 금색은 한 번도
    // 나오지 않는다. 도달 레벨 기준이라야 동(Lv0 카드) → 은(Lv1) → 금(Lv2, 고르면 만렙)이 다 쓰인다.
    private Color ResolveTint(SkillEffectSnapshot snapshot)
    {
        Color[] palette = levelColors != null && levelColors.Length > 0
            ? levelColors
            : DefaultLevelColors;

        // 효과가 없으면 NextLevel이 0이라 -1이 되는데 Clamp가 0으로 받아낸다(배열 밖 접근 방지).
        return palette[Mathf.Clamp(snapshot.NextLevel - 1, 0, palette.Length - 1)];
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
        boundCandidates = null;
    }

    private void SelectReward(WaveRewardData reward)
    {
        selectionSource?.TrySetResult(reward);
    }
}
