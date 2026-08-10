using UnityEngine;

// 스킬 특수효과 종류(#169). 보상은 전부 스킬 특수효과라 타입별로 SkillEffectManager가 레벨을 관리한다.
// 새 타입 추가 시 기존 에셋의 직렬화 값(int) 보존을 위해 반드시 뒤에만 추가할 것.
public enum WaveRewardType
{
    Burn,
    Bomb,
    ExtraCast,
    BuffBurn,
    Field,
    Execute
}

[CreateAssetMenu(fileName = "WaveReward",menuName = "Scriptable Objects/Wave Reward"
)]
public class WaveRewardData : ScriptableObject
{
    [Header("Display")]
    [SerializeField]
    private string displayName;

    [SerializeField]
    private string description;

    [SerializeField]
    private Sprite icon;

    [Header("Effect")]
    [SerializeField]
    private WaveRewardType rewardType;

    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public WaveRewardType RewardType => rewardType;
}