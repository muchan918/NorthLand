using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 3택1 보상 카드 한 장의 뷰(#320). 카드 프리팹 루트에 붙는다.
//
// 이 클래스는 "보상 하나를 카드에 어떻게 그리는가"만 안다 — 후보를 몇 장 띄울지,
// 어느 레벨에 어느 카드면을 쓸지는 상위(WaveRewardSelectionUI)가 정해서 Bind로 넘긴다.
// (SkillVisualSet이 레벨→프리팹 매핑을 소유하고 SkillManager는 조회만 하는 것과 같은 분리)
//
// 도입 전에는 WaveRewardSelectionUI가 카드 요소를 평행 배열 6개로 들고 같은 i로 인덱싱했다.
// 배열 순서가 어긋나면 예외도 경고도 없이 엉뚱한 카드에 값이 들어가고, 카드에 요소를 하나
// 추가할 때마다 배열 1개 + 씬 배선 3개가 늘었다.
public class RewardCardView : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField]
    private Button button;

    [Header("Graphics")]
    // 카드면. 레벨별 카드 에셋으로 교체하는 자리(#320) — 미배선이면 조용히 건너뛴다.
    // 에셋이 준비되면 코드 수정 없이 인스펙터 배선만으로 켜진다.
    [SerializeField]
    private Image cardFace;

    [SerializeField]
    private Image icon;

    [Header("Texts")]
    [SerializeField]
    private TextMeshProUGUI nameText;

    // 설명 + 줄바꿈 + 수치 요약(#287)을 한 곳에 담는다.
    [SerializeField]
    private TextMeshProUGUI descriptionText;

    [Header("Level Stars")]
    // 왼쪽부터 순서대로. 채워지는 개수 = 이 카드를 고르면 도달할 레벨(GetNextLevel).
    [SerializeField]
    private Image[] starImages;

    [SerializeField]
    private Sprite starOn;

    [SerializeField]
    private Sprite starOff;

    private Action<WaveRewardData> onSelect;
    private WaveRewardData boundReward;

    /// 보상 하나를 이 카드에 표시한다. faceTint는 등급 색(RGB만 쓰고 알파는 프리팹 값을 유지).
    public void Bind(WaveRewardData reward, Color faceTint, Action<WaveRewardData> selectCallback)
    {
        boundReward = reward;
        onSelect = selectCallback;

        // 리스너 등록을 Awake가 아니라 여기서 한다 — 카드가 비활성 계층(Rewardpanel) 아래에서
        // Instantiate되므로 Awake는 Bind보다 늦게(패널이 켜질 때) 돈다. 초기화를 Awake에 두면
        // 이 뷰의 불변식이 호출부의 활성화 순서에 의존하게 된다. 인스턴스당 Bind 1회지만
        // 재바인딩(로케일 변경) 시 누적되지 않도록 Remove를 먼저 부른다.
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
        }

        if (reward == null)
        {
            return;
        }

        if (nameText != null)
        {
            nameText.text = LocalizationHelper.Get(LocalizationHelper.k_RewardsTable, reward.DisplayName);
        }

        if (icon != null)
        {
            icon.sprite = reward.Icon;
            icon.enabled = reward.Icon != null;
        }

        // 실제 보유 레벨을 그대로 보여준다 — 미보유는 Lv 0, 한 번 고르면 Lv 1.
        // 하한 클램프를 걸면 Lv0과 Lv1의 표시가 겹쳐서 첫 획득이 화면상 아무 변화가 없어 보인다.
        // 다음 레벨은 UI가 +1로 계산하지 않고 효과에게 묻는다 — 상한(#292)이 여기 반영돼야 한다.
        int level = 0;
        int nextLevel = 0;
        bool nextIsMax = false;
        string stats = null;

        if (SkillEffectManager.Instance != null)
        {
            level = SkillEffectManager.Instance.GetLevel(reward.RewardType);
            nextLevel = SkillEffectManager.Instance.GetNextLevel(reward.RewardType);
            nextIsMax = SkillEffectManager.Instance.ReachesMaxLevel(reward.RewardType);
            stats = SkillEffectManager.Instance.GetStatSummary(reward.RewardType);
        }

        // 수치가 비었다 = 이 타입의 효과 컴포넌트가 없다 = 선택해도 SkillEffectManager가
        // 경고만 내고 무시한다. 그때 레벨·별만 남기면 "고르면 오른다"는 거짓 표시가 되므로
        // 통째로 비워 씬 배선 사고가 화면에 드러나게 한다.
        bool hasStats = !string.IsNullOrEmpty(stats);

        // 등급 색도 위 규약 안에 둔다 — 별이 0개인데 카드면만 Lv1 색으로 칠하면 표시 세 요소
        // (별·레벨 줄·색) 중 하나가 규약을 벗어난다. 미표시일 땐 프리팹 기본색을 유지한다.
        //
        // 알파는 프리팹이 정한 값을 그대로 둔다 — 카드면의 투명도는 등급이 아니라 디자인의 몫이고,
        // 넘겨받은 색의 알파까지 적용하면 등급 색을 바꿀 때마다 투명도가 딸려 바뀐다.
        if (cardFace != null && hasStats)
        {
            cardFace.color = new Color(faceTint.r, faceTint.g, faceTint.b, cardFace.color.a);
        }

        if (descriptionText != null)
        {
            string description = LocalizationHelper.Get(LocalizationHelper.k_RewardsTable, reward.Description);

            descriptionText.text = hasStats
                ? $"{description}\n{SkillStatsFormatter.BuildLevelLine(level, nextLevel, nextIsMax)}\n{stats}"
                : description;
        }

        ApplyStars(hasStats ? nextLevel : 0);
    }

    // 왼쪽부터 filled개를 켜고 나머지는 끈다. 스프라이트가 미배선이면 그 상태를 유지한다
    // (별 자리가 사라지면 레이아웃이 흔들리므로 오브젝트를 끄지는 않는다).
    private void ApplyStars(int filled)
    {
        if (starImages == null)
        {
            return;
        }

        for (int i = 0; i < starImages.Length; i++)
        {
            if (starImages[i] == null)
            {
                continue;
            }

            Sprite target = i < filled ? starOn : starOff;

            if (target != null)
            {
                starImages[i].sprite = target;
            }
        }
    }

    private void HandleClick()
    {
        if (boundReward != null)
        {
            onSelect?.Invoke(boundReward);
        }
    }
}
