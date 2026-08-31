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

        // 폭발 연출(#452). 지정하면 **사망 모션을 건너뛰고 즉시 사라진다** — 「터져서 없어진다」가
        // 자폭병의 연출이고, 2초짜리 패배 모션이 뒤에 붙으면 터진 몸이 천천히 쓰러진다.
        // 비워두면 예전대로 사망 모션을 재생한다(즉시 사라지면 아무 피드백이 없다).
        [Tooltip("폭발 순간 스폰할 파티클 프리팹. playOnAwake가 켜져 있어야 한다 — " +
                 "Instantiate만으로 재생을 시작한다. 비우면 파티클 없이 사망 모션을 재생한다.")]
        public GameObject ExplosionVfx;

        [Tooltip("파티클 프리팹의 스케일에 곱하는 배수. 1 = 프리팹에 저작된 크기 그대로. " +
                 "@NorthLand/Particles의 폭발들은 스킬 광역 기준으로 커져 있어(FX_Bomb_Exp 17, " +
                 "FX_Fire_Exp 20) 자폭병 한 마리(높이 약 5.8)에는 줄여야 할 수 있다.")]
        public float ExplosionScale = 1f;

        [Tooltip("파티클 오브젝트를 제거하기까지의 시간(초). 파티클 최대 수명보다 길게 잡는다 — " +
                 "짧으면 퍼지던 입자가 잘린다(FX_Bomb_Exp 약 5.6s, FX_Fire_Exp 약 4.6s).")]
        public float ExplosionLifetime = 5f;

        // 폭발음(#452). **클립을 SfxBank에 넣지 않는다** — 뱅크의 범위는 "주인이 없는 공용 소리"이고
        // (`Docs/Core/AudioManager.md` §5.4) 이 소리의 주인은 이 EnemyAsset이다. 타워 발사음을
        // 각자의 SO가 들기로 한 것과 같은 규칙이다(같은 문서 §6.3).
        //
        // 재생 경로는 **위치 기반 풀(`CombatSfx`, §6.2)**이다 — 예전 2D 원샷에서 옮겼다.
        // 2D는 카메라가 본진에 없을 때 그림 없이 폭음만 내보냈다. 「본진이 맞았다」는 통지는
        // `PlayerBase`의 피격음이 같은 감쇠 규칙으로 맡는다(§6.4).
        // ⚠ **몬스터 평타음을 여기에 얹지 말 것** — 평타는 본진 피격음이 이미 한 창구에서 낸다.
        [Tooltip("폭발 순간 1회 재생할 효과음. 비우면 소리 없이 폭발한다. " +
                 "화면 밖·줌아웃에서는 들리지 않는다(AudioManager.md §6.2).")]
        public AudioClip ExplosionSfx;

        [Range(0f, 2f)]
        [Tooltip("폭발음 재생 배율. SFX 채널 볼륨에 곱해진다. 임포트 설정에는 클립별 게인이 없어 " +
                 "(AudioManager.md §4.5) 클립 사이의 레벨 차는 여기서만 맞출 수 있다. " +
                 "화면 감쇠가 헤드룸을 먹으므로 2D 시절 값보다 올려야 할 수 있다(상한 2).")]
        public float ExplosionSfxVolume = 1f;
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

        // 파티클을 스폰한 프레임에 다시 지우면 **폭발이 아예 보이지 않는다** — 증상이
        // "프리팹을 넣었는데 아무 일도 안 난다"라 원인에서 멀다(#452).
        if (SelfDestruct.ExplosionVfx != null && SelfDestruct.ExplosionLifetime <= 0f)
        {
            Debug.LogWarning($"[EnemyAsset] {name}: ExplosionVfx가 지정됐는데 ExplosionLifetime이 " +
                             $"{SelfDestruct.ExplosionLifetime}입니다 — 폭발이 보이지 않습니다.", this);
        }

        // 자폭 순간에는 본진 피격음이 **일부러 억제된다**(`PlayerBase.PlayHitSfx`, AudioManager.md §6.4)
        // — 폭발음이 그 자리의 피드백을 다 하기 때문이다. 그래서 이 클립이 비면 대신 나 주는 소리가
        // 없어 **자폭이 완전 무음이 된다.** 증상이 "본진이 소리 없이 깎인다"라 원인에서 멀다.
        if (SelfDestruct.ExplosionSfx == null)
        {
            Debug.LogWarning($"[EnemyAsset] {name}: 자폭이 켜져 있는데 ExplosionSfx가 비어 있습니다 — " +
                             "자폭 순간 본진 피격음도 억제되므로 완전 무음으로 터집니다.", this);
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
