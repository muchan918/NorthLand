using UnityEngine;

[CreateAssetMenu(fileName = "EnemyAsset", menuName = "Scriptable Objects/EnemyAsset")]
public class EnemyAsset : ScriptableObject
{
    public string EnemyID;

    // BuildingAsset/TowerAsset과 동일한 이유로 Data 캐시가 아닌 일반 필드로 노출한다
    // (EnemyAssetEditor가 Play 이전 편집 모드에서 타입별 필드 그룹을 골라 보여줘야 함).
    public EnemyType EnemyType;

    [HideInInspector]
    public EnemyData Data;

    public MeleeFields Melee;
    public RangedFields Ranged;
    public BossFields Boss;

    // Melee/Ranged/Boss 공통 기초 전투 스탯. Combat/EnemyData.cs(SUNGSOO)의
    // maxHp/attackDamage/attackRange/attackInterval과 의미 대응되도록 필드명을 맞춘다
    // (실제 Combat 마이그레이션은 아직 미착수, WL-001).
    [System.Serializable]
    public class CombatFields
    {
        public float MaxHp;
        public float MoveSpeed;
        public float AttackDamage;
        public float AttackRange;
        public float AttackInterval;
    }

    [System.Serializable]
    public class MeleeFields
    {
        public CombatFields Stat;
    }

    [System.Serializable]
    public class RangedFields
    {
        public CombatFields Stat;
        public GameObject ProjectilePrefab;
        public float ProjectileSpeed;
    }

    [System.Serializable]
    public class BossFields
    {
        public CombatFields Stat;

        // TODO: 실제 BehaviorTree 에셋 타입이 정해지면 교체
        // (예: com.unity.behavior의 BehaviorGraph, 또는 커스텀 SO)
        public Object BehaviorTree;
    }
}
