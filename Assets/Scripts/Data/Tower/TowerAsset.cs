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
        // >0이면 투사체 명중 시 대상에 스턴(초) 부여(#164 소다타워). 0=없음. 슬로우 인프라(StatusEffectHandler) 재사용.
        public float OnHitStunDuration;
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
        // 체인은 히트스캔(빔)이라 Attack.ProjectilePrefab/ProjectileSpeed를 사용하지 않는다(#252). AttackFields를 계속 공유하는 이유는 AttackDamage/Range/Interval이 전달 방식과 무관하고, TowerBehaviourFactory.ResolveAttackFields가 그 세 값의 단일 출처이기 때문(WL-079).
        // Attack.OnHitStunDuration도 사용하지 않는다 — **체인은 스턴·화상을 받지 않는 것으로 확정**(#252). 번개 테마상 발화·기절이 성립하지 않고, 1발에 최대 MaxChainTargets명을 때려 CC/DoT 처리량이 단일 타격 기준 튜닝을 무너뜨린다. 지원하려면 ChainResolver.Resolve에 지속시간 파라미터를 더해 홉마다 부여하는 형태여야 하며 기획 결정이 선행한다. 화상 제외는 BurnBuff가 Has<AttackBehaviour>()로 거른다.
        public AttackFields Attack;
        public GameObject BeamPrefab;   // 빔 연출 프리팹(LineRenderer 저작용). null이면 코드가 런타임에 기본 빔을 생성 — 아트 머티리얼을 기다리지 않고 검증할 수 있게 한 폴백.
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
