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
    // 왼쪽부터 순서대로. 채워지는 개수 = 지금 보유한 레벨(GetLevel)이고, 그 다음 한 칸은
    // 이번 선택으로 켜질 자리라 반투명 미리보기로 보여준다.
    //
    // 도입 전에는 GetNextLevel 개수만큼 켰다(#320) — "고르면 몇 레벨이 되는가"를 보여주려던
    // 규약이지만, 같은 카드의 레벨 줄이 "Lv 0 → Lv 1"로 현재값을 함께 보여주면서 미보유 카드에
    // 별이 1개 켜진 채로 떴다. 한 장의 카드가 두 개의 레벨을 동시에 주장하던 셈(#353).
    [SerializeField]
    private Image[] starImages;

    [SerializeField]
    private Sprite starOn;

    [SerializeField]
    private Sprite starOff;

    // 미리보기 칸의 알파. 인스펙터 필드가 아니라 코드 상수로 두는 근거는
    // WaveRewardSelectionUI.DefaultLevelColors와 같다 — 이미 직렬화된 컴포넌트엔 필드
    // 초기화자가 안 먹으므로, 값이 실행 경로에 있어야 씬 배선 없이도 동작한다.
    private const float k_PreviewAlpha = 0.45f;

    private Action<WaveRewardData> onSelect;
    private WaveRewardData boundReward;

    /// 보상 하나를 이 카드에 표시한다. snapshot은 이 보상의 레벨·수치 한 벌이고(#353),
    /// faceTint는 등급 색(RGB만 쓰고 알파는 프리팹 값을 유지).
    public void Bind(
        WaveRewardData reward,
        SkillEffectSnapshot snapshot,
        Color faceTint,
        Action<WaveRewardData> selectCallback)
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
        // 다음 레벨은 UI가 +1로 계산하지 않고 효과가 정해 스냅샷에 담아 온다 — 상한(#292)이
        // 여기 반영돼야 한다.
        //
        // 수치가 비었다 = 이 타입의 효과 컴포넌트가 없다(또는 매니저 자체가 없어 호출부가 default를
        // 넘겼다) = 선택해도 SkillEffectManager가 경고만 내고 무시한다. 그때 레벨·별만 남기면
        // "고르면 오른다"는 거짓 표시가 되므로 통째로 비워 씬 배선 사고가 화면에 드러나게 한다.
        bool hasStats = !string.IsNullOrEmpty(snapshot.Stats);

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
                ? $"{description}\n" +
                  $"{SkillStatsFormatter.BuildLevelLine(snapshot.Level, snapshot.NextLevel, snapshot.NextIsMax)}\n" +
                  $"{snapshot.Stats}"
                : description;
        }

        // hasStats가 false면 (0, 0)이라 미리보기 칸도 그려지지 않는다 — 위 규약대로 통째로 빈다.
        ApplyStars(hasStats ? snapshot.Level : 0, hasStats ? snapshot.NextLevel : 0);
    }

    // 왼쪽부터 current개를 켜고, 그 다음 한 칸을 반투명 미리보기로, 나머지는 끈다.
    // 스프라이트가 미배선이면 그 상태를 유지한다(별 자리가 사라지면 레이아웃이 흔들리므로
    // 오브젝트를 끄지는 않는다).
    private void ApplyStars(int current, int next)
    {
        if (starImages == null)
        {
            return;
        }

        // next > current여야 미리보기 칸이 있다. 만렙 효과는 후보에서 빠지므로(#292) 실전에선
        // 항상 참이지만, 디버그 패널로 상한까지 올린 뒤 보상 창을 띄우면 없는 칸을 그리려 든다.
        bool hasPreview = next > current;

        // 별 칸 수와 효과 상한(SkillEffect.maxLevel)은 서로 다른 저장소가 소유한다 — 칸은
        // NorthLand-Imported의 Card.prefab이, 상한은 GameScene의 인스펙터가 갖는다. 상한을 인스펙터에서
        // 올리는 것만으로 도달 레벨이 칸 수를 넘어서고, 그때 마지막 카드는 별이 꽉 찬 채 미리보기가
        // 사라지는데 레벨 줄만 "Lv 3 → Lv 4"라고 말한다 — #353이 고친 "한 장이 두 레벨을 주장한다"가
        // 같은 형태로 재발한다. 코드에도 프리팹에도 diff가 남지 않아 리뷰로는 잡히지 않으므로
        // 여기서 콘솔에 드러낸다(카드당 1회).
        if (next > starImages.Length)
        {
            Debug.LogWarning(
                $"[RewardCardView] 별 칸이 모자랍니다: 칸 {starImages.Length}개 < 도달 레벨 {next}. " +
                $"카드 프리팹의 별 개수를 SkillEffect.maxLevel에 맞추세요.",
                this);
        }

        for (int i = 0; i < starImages.Length; i++)
        {
            if (starImages[i] == null)
            {
                continue;
            }

            bool isPreview = hasPreview && i == current;

            // 미리보기는 starOff가 아니라 starOn을 흐리게 쓴다 — 꺼진 별을 반투명하게 만들면
            // "곧 켜질 자리"가 아니라 그냥 더 흐린 빈 칸으로 읽힌다.
            Sprite target = i < current || isPreview ? starOn : starOff;

            if (target != null)
            {
                starImages[i].sprite = target;
            }

            // RGB는 프리팹 값을 유지하고 알파만 교체한다 — 카드면(cardFace)이 알파를 유지하고
            // RGB만 바꾸는 것의 정확한 반대다. 별의 켜짐/꺼짐은 상태 표현이라 코드가 소유하고,
            // 색조는 디자인의 몫이기 때문.
            //
            // 스프라이트 널 가드 밖에 두는 이유: Bind에 대한 멱등성이다. 알파와 스프라이트 배선은
            // 별개 축이라 같은 가드를 공유하면, starOn/starOff가 미배선인 카드에서만 알파가 직전
            // 호출 값에 머문다. 지금은 카드가 매번 새 인스턴스라(ClearCards가 파괴 후 재생성)
            // 드러나지 않지만, 파괴 대신 Bind를 다시 부르는 최적화가 들어오면 그때 깨진다.
            Color color = starImages[i].color;
            starImages[i].color = new Color(color.r, color.g, color.b, isPreview ? k_PreviewAlpha : 1f);
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
