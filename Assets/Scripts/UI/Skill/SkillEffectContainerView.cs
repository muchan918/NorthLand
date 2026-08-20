using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// 획득한 특수효과 한 칸(#397). 아이콘은 보상이 들고 있고, 레벨은 틀(Container) 스프라이트로 나타낸다.
// 레벨→아트 매핑은 인스펙터가 소유하고 코드는 인덱스만 계산한다 — RewardCardView.ApplySkin과 같은 규약(#356).
public class SkillEffectContainerView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("레벨에 따라 스프라이트가 갈리는 틀.")]
    [SerializeField] Image _container;

    [Tooltip("보상 아이콘이 들어갈 자리. 스프라이트는 런타임에 채운다.")]
    [SerializeField] Image _icon;

    [Tooltip("보유 레벨별 틀 스프라이트. 인덱스 = 레벨 - 1.")]
    [SerializeField] Sprite[] _levelSprites;

    // Bind가 받은 값을 호버 시점에 쓰려고 들고 있는다. _owner는 프리팹에 배선할 수 없어
    // (프리팹은 씬 오브젝트 참조를 직렬화하지 못한다) 생성한 쪽이 코드로 넘겨준다.
    WaveRewardData _reward;
    int _level;
    SkillEffectPanelView _owner;

    public void Bind(WaveRewardData reward, int level, SkillEffectPanelView owner)
    {
        _reward = reward;
        _level = level;
        _owner = owner;

        if (_icon != null)
        {
            _icon.sprite = reward != null ? reward.Icon : null;
            _icon.enabled = _icon.sprite != null;
        }

        ApplyLevelSprite(level);
    }

    // 인덱스 기준은 보유 레벨(level - 1)이다. 보상 카드는 미리보기라 도달 레벨(NextLevel - 1)을
    // 쓰는데, 그걸 따라가면 아직 얻지도 않은 등급의 틀이 뜬다 — 한 화면이 두 레벨을 주장하는 #353 재발.
    void ApplyLevelSprite(int level)
    {
        if (_container == null || _levelSprites == null || _levelSprites.Length == 0) return;

        int index = level - 1;

        // 상한(SkillEffect.maxLevel)은 씬 인스펙터가, 스프라이트 장수는 프리팹이 소유한다.
        // 어긋나도 코드에도 프리팹에도 diff가 안 남으므로 콘솔에 드러낸다.
        if (index >= _levelSprites.Length)
        {
            Debug.LogWarning(
                $"[SkillEffectContainer] 레벨 스프라이트가 모자랍니다: " +
                $"{_levelSprites.Length}장 < 레벨 {level}. SkillEffect.maxLevel에 맞추세요.",
                this);
        }

        Sprite sprite = _levelSprites[Mathf.Clamp(index, 0, _levelSprites.Length - 1)];

        if (sprite != null)
            _container.sprite = sprite;
    }

    public void OnPointerEnter(PointerEventData eventData) => _owner?.ShowTooltip(_reward, _level);

    public void OnPointerExit(PointerEventData eventData) => _owner?.HideTooltip();
}