using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TowerAsset", menuName = "Scriptable Objects/TowerAsset")]
public class TowerAsset : ScriptableObject
{
    public string TowerID;

    // BuildingAsset과 동일한 이유로 Data 캐시가 아닌 일반 필드로 노출한다
    // (TowerAssetEditor가 Play 이전 편집 모드에서 타입별 필드 그룹을 골라 보여줘야 함).
    public TowerType TowerType;
    public MagicEffectType MagicEffectType;

    [HideInInspector]
    public TowerData Data;

    public List<ResourceCost> Cost;

    public SingleFields Single;
    public AreaFields Area;
    public ChainFields Chain;
    public MagicFields Magic;

    // Single/Area/Chain 공통 공격 스탯. Combat/TowerData.cs(SUNGSOO)의 필드와 의미 대응되도록
    // 맞춰서, 추후 Combat이 이 파이프라인으로 옮겨올 때 매핑이 쉽도록 한다.
    [System.Serializable]
    public class AttackFields
    {
        public float AttackDamage;
        public float AttackRange;
        public float AttackInterval;
        public GameObject ProjectilePrefab;
        public float ProjectileSpeed;
    }

    [System.Serializable]
    public class SingleFields
    {
        public AttackFields Attack;
    }

    [System.Serializable]
    public class AreaFields
    {
        public AttackFields Attack;
        public float SplashRadius;
    }

    [System.Serializable]
    public class ChainFields
    {
        public AttackFields Attack;
        public float ChainRadius;
        public int MaxChainTargets;
        public float ChainDamageFalloff;
    }

    [System.Serializable]
    public class MagicFields
    {
        public BuffAuraFields BuffAura;
        public DebuffAuraFields DebuffAura;
    }

    [System.Serializable]
    public class BuffAuraFields
    {
        public float Radius;
        public float Interval;
        public List<StatModifier> Modifiers;
        public OptionalDamage Damage;
    }

    [System.Serializable]
    public class DebuffAuraFields
    {
        public float Radius;
        public float Interval;
        public List<StatModifier> Modifiers;
        public OptionalDamage Damage;
    }
}

public enum ModifiableStat
{
    AttackDamage,
    AttackSpeed,
    MoveSpeed,
    Armor,
}

[System.Serializable]
public class StatModifier
{
    public ModifiableStat Stat;
    public float Amount;
    public bool IsPercentage;
}

[System.Serializable]
public class OptionalDamage
{
    public bool HasDamage;
    public float DamageAmount;
    public float TickInterval;
}
