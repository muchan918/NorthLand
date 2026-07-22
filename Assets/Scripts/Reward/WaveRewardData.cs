using UnityEngine;

public enum WaveRewardType
{
    Fire,
    Ice,
    Lightning,
    Poison
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

    [Min(1)]
    [SerializeField]
    private int amount = 1;

    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public WaveRewardType RewardType => rewardType;
    public int Amount => amount;
}