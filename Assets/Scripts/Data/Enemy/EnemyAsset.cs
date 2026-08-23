using UnityEngine;

[CreateAssetMenu(fileName = "EnemyAsset", menuName = "Scriptable Objects/EnemyAsset")]
public class EnemyAsset : ScriptableObject
{
    public string EnemyID;

    [Header("UI")]
    [SerializeField] private Sprite icon;
    public Sprite Icon => icon;

    // BuildingAsset/TowerAsset과 동일한 이유로 Data 캐시가 아닌 일반 필드로 노출한다
    // (EnemyAssetEditor가 Play 이전 편집 모드에서 타입별 필드 그룹을 골라 보여줘야 함).
    public EnemyType EnemyType;
    public MovementMode MovementMode;

    [HideInInspector]
    public EnemyData Data;

    public MeleeFields Melee;
    public RangedFields Ranged;
    public BossFields Boss;

    // 자폭(#453). **EnemyType 축과 직교한다** — 자폭병도 스탯은 자기 EnemyType의 블록(현재 Melee)을
    // 그대로 쓰고, "본진에 닿으면 터진다"는 성질만 여기서 얹는다.
    //
    // EnemyType에 값을 늘리지 않은 이유: 그 필드는 스탯 블록 선택과 근접/원거리 공격 경로 선택을
    // 겸하고 있어서(WL-207) 값을 하나 더하면 스탯 블록·EnemyAssetEditor·TableImporter·CSV 파싱이
    // 함께 갈라진다. 자폭은 그 갈림 어디에도 새 분기를 요구하지 않는다.
    public SelfDestructFields SelfDestruct;

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

    // 자폭 저작 필드(#453). Enabled를 끄면 일반 근접 몬스터와 완전히 같으므로,
    // 기존 EnemyAsset 8종은 이 블록이 직렬화에 없어도(기본값 false/0) 거동이 바뀌지 않는다.
    [System.Serializable]
    public class SelfDestructFields
    {
        [Tooltip("본진에 닿는 순간 자폭한다. 켜면 이 몬스터는 본진 외의 대상을 조준하지 않는다.")]
        public bool Enabled;

        [Tooltip("자폭 1회로 본진에 주는 확정 피해. 웨이브 HP 배율의 영향을 받지 않는다 — " +
                 "규약 ④의 자폭 위험 예산(웨이브당 총량 ≤ 본진 HP×0.5)이 성립하는 근거다.")]
        public float Damage;
    }

    // 저작 짝 검사(WL-205). 두 조합이 **아무 신호 없이** 실패하므로 저장 시점에 드러낸다 —
    // 증상이 "자폭병이 있는데 본진이 안 깎인다" / "0인 게 이상해서 채웠는데 아무 일도 안 난다"라
    // 원인에서 멀다. 같은 유형의 무증상 조합을 TowerAsset.OnValidate가 이미 잡고 있어 방식을 맞춘다.
    //
    // 경고로만 낸다(값을 고치지 않는다) — 저작 중간 상태를 에디터가 되돌리면 입력이 막힌다.
    void OnValidate()
    {
        if (SelfDestruct == null || !SelfDestruct.Enabled)
        {
            return;
        }

        if (SelfDestruct.Damage <= 0f)
        {
            Debug.LogWarning($"[EnemyAsset] {name}: 자폭이 켜져 있는데 SelfDestruct.Damage가 " +
                             $"{SelfDestruct.Damage}입니다 — 본진까지 달려가 0 피해를 주고 사라집니다.", this);
        }

        CombatFields stat = EnemyType switch
        {
            EnemyType.Melee  => Melee?.Stat,
            EnemyType.Ranged => Ranged?.Stat,
            EnemyType.Boss   => Boss?.Stat,
            _ => null,
        };

        // 자폭병은 본진만 조준하고 닿는 즉시 터지므로 평타 경로(Enemy.TryAttack)에 도달하지 않는다.
        // 즉 AttackDamage는 **영구히 읽히지 않는 값**이다 — 채워두면 "설정했는데 안 먹는다"가 된다.
        if (stat != null && stat.AttackDamage != 0f)
        {
            Debug.LogWarning($"[EnemyAsset] {name}: 자폭병의 AttackDamage({stat.AttackDamage})는 " +
                             "읽히지 않습니다 — 자폭병은 평타 경로를 타지 않습니다. 0으로 두세요.", this);
        }
    }

    [System.Serializable]
    public class BossFields
    {
        public CombatFields Stat;

        // 이 보스가 실행할 BehaviorTree 런타임 그래프. Enemy가 Awake에서 BehaviorGraphAgent에 주입한다
        // (데이터 주도 — 프리팹에 에이전트·그래프를 수동 배선하지 않는다).
        // 지정: Project에서 그래프 에셋(예: MidBossBehavior)을 펼쳐 안의 BehaviorGraph 서브에셋을 이 칸에 드래그.
        public Unity.Behavior.BehaviorGraph BehaviorTree;
    }
}
