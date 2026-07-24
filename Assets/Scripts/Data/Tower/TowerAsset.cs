using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TowerAsset", menuName = "Scriptable Objects/TowerAsset")]
public class TowerAsset : ScriptableObject
{
    public string TowerID;
    public GameObject TowerPrefab;
    public GameObject GhostPrefab; // 배치 전 미리보기용 투명 타워 프리팹. TowerPrefab과 동일한 구조를 가져야 한다.

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

    // 마법 타워의 오라 반경(=사거리) 단일 출처(WL-056). MagicEffectType으로 분기.
    // 실효과 반경(AuraTower)과 배치 프리뷰 반경(TowerPlacer)이 공통으로 이 값을 읽어
    // 두 곳에 규칙이 중복돼 조용히 어긋나는 것을 막는다.
    public float MagicRadius => Magic == null ? 0f : MagicEffectType switch
    {
        MagicEffectType.Debuff => Magic.DebuffAura != null ? Magic.DebuffAura.Radius : 0f,
        MagicEffectType.Buff   => Magic.BuffAura   != null ? Magic.BuffAura.Radius   : 0f,
        _ => 0f,
    };

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
        // 버프는 이벤트형(배치 즉시 부여 + 타워 추가/제거 시 재적용, 범위 유지형)이라 Interval/Duration이 불필요(#164). Radius+Modifiers만 사용한다.
        public float Radius;
        public List<StatModifier> Modifiers;
        public OptionalDamage Damage;   // (현재 미사용) 향후 아군 힐 등 확장 여지로 보존
    }

    [System.Serializable]
    public class DebuffAuraFields
    {
        public float Radius;
        public float Interval;
        public float Duration;
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
