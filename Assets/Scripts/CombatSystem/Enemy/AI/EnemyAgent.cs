using NorthLand.Combat;
using UnityEngine;

// 보스 BT 커스텀 리프 노드가 참조하는 유일한 컴포넌트(#233).
//
// Enemy를 상속하지 않고 같은 오브젝트에 나란히 부착한다(병존). 그래서
//  · 잡몹 프리팹에도 이 컴포넌트만 추가하면 같은 노드를 그대로 재사용할 수 있고
//  · Enemy의 private 멤버(movement / monsterStateMachine / FindTarget)를 열 필요가 없고
//  · Enemy.Awake의 BehaviorGraphAgent 주입 경로와 Awake 오버라이드 순서를 신경 쓰지 않아도 된다.
//
// 무상태 파사드다. 패턴 이동속도 배수는 MonsterMove가, 받는 피해 배수는 Enemy가 소유하고
// 여기서는 전달만 한다 — 양쪽이 같은 값을 들면 동기화가 깨진다.
// 유일한 예외는 패턴 쿨다운 기록(EnemyPatternMemory)이다.
//
// 보스별 고유 능력이 필요하면 이 클래스를 상속한 파생 컴포넌트를 쓴다.
// 노드 입력 타입이 EnemyAgent이므로 파생 타입이 그대로 들어간다.
//
// 네임스페이스를 두지 않는다 — 커스텀 노드와 같은 규약
// (Docs/Monster/Boss/BossNodeReference.md 「작성 규약」). 클래스 이름이 전역에서 유일해야 한다.
public class EnemyAgent : MonoBehaviour
{
    // 씬에 직접 배치한 테스트용 보스를 위한 칸. 런타임 스폰은 MonsterSpawn이 BindSpawner로 주입한다.
    [Tooltip("소환 패턴이 사용할 스포너. 비워두면 스폰 시점에 자동 주입된다(씬 배치 테스트용 칸).")]
    [SerializeField] private MonsterSpawn spawner;

    // 반경 질의(주변 잡몹 수 / 최근접 대상)의 물리 프리필터.
    // LayerMask는 Unity.Behavior가 지원하는 Blackboard 변수 타입이 아니라 노드 입력으로 받을 수 없다.
    // 그래서 마스크는 프리팹 인스펙터에서 authoring하고, 진영 구분은 아래 Faction으로 사후 필터한다
    // (Enemy.targetLayerMask와 같은 계보).
    // 넓게 잡는 것이 맞다. 이 마스크는 "질의 후보 집합"이고 아군/적군 판정은
    // EnemyNodeQuery.TryAccept가 IDamageable.Faction으로 사후에 한다. 마스크에서 빠진 레이어는
    // 진영 필터에 닿기도 전에 사라지므로, 부분적으로 비어 있으면 Hostile 조건이
    // 예외도 로그도 없이 항상 0을 반환한다 — 값이 아예 비었을 때보다 찾기 어렵다.
    [Tooltip("주변 대상 질의의 후보 레이어. Enemy(7) + Soldier(8) + PlayerBase(9)를 모두 포함할 것. " +
             "빠진 레이어는 진영 판정 전에 걸러져 조용한 무동작이 된다.")]
    [SerializeField] private LayerMask unitLayerMask;

    private Enemy enemy;

    // 구체 타입이 아니라 계약에 의존한다 — Enemy와 같은 이유(이동 구현에 결합하지 않는다).
    private IMovementAgent movement;

    // 진행 방향 판정용. MonsterMove가 자기 transform을 회전시키므로
    // 앞뒤 판정의 기준은 루트가 아니라 이동 컴포넌트가 붙은 transform이다.
    private Transform facing;

    // MonsterAnimation은 IsMove / IsAttack / IsDie Bool 3개만 노출하고 임의 클립을 재생할 수단이 없다.
    // Animator를 여기서 직접 들면 MonsterAnimation을 수정하지 않아도 된다.
    private Animator animator;

    private readonly EnemyPatternMemory patternMemory = new EnemyPatternMemory();

    private void Awake()
    {
        // 조상까지 탐색: 이 컴포넌트를 모델 자식에 붙여도 루트의 Enemy를 찾아야 한다.
        // MonsterSpawn의 주입이 GetComponentInChildren<EnemyAgent>로 자식까지 훑기 때문에
        // GetComponent로 좁히면 "주입은 성공했는데 enemy만 null"인 조합이 생기고,
        // 받는 피해 배수·이동 소유권·HP 조건이 조용히 무동작한다. 탐색 범위를 넓게
        // 통일한다는 WL-093의 판단과 같은 방향이다.
        enemy = GetComponentInParent<Enemy>();

        // 자식까지 탐색(WL-093): MonsterMove·Animator가 자식 오브젝트에 붙는 프리팹이 있다.
        movement = GetComponentInChildren<IMovementAgent>();
        animator = GetComponentInChildren<Animator>();

        facing = movement is Component movementComponent
            ? movementComponent.transform
            : transform;

        // 조용한 무동작을 막는다 — 참조가 빠지면 노드는 성공을 반환하는데 아무 일도 일어나지 않는다.
        if (enemy == null)
        {
            Debug.LogWarning($"[{name}] Enemy를 찾지 못해 받는 피해 배수·이동 소유권·HP 조건 노드가 동작하지 않습니다.", this);
        }

        if (movement == null)
        {
            Debug.LogWarning($"[{name}] IMovementAgent를 찾지 못해 이동속도·정지 관련 노드가 동작하지 않습니다.", this);
        }

        if (animator == null)
        {
            Debug.LogWarning($"[{name}] Animator를 찾지 못해 애니메이션 재생 노드가 동작하지 않습니다.", this);
        }
    }

    // ── 이동속도 ─────────────────────────────

    // 패턴 축(돌진 가속 / 방어 태세 크롤). 감속 타워의 디버프 축과 곱해지는 별도 축이다.
    public float PatternSpeedFactor
    {
        get => movement != null ? movement.PatternSpeedFactor : 1f;
        set
        {
            if (movement == null)
            {
                return;
            }

            movement.PatternSpeedFactor = value;
        }
    }

    // 디버프까지 반영된 최종 이동속도. 돌진 충돌 피해는 배수가 아니라 이 값을 입력으로 써야
    // "감속 타워로 돌진을 파훼한다"가 성립한다.
    public float EffectiveMoveSpeed => movement != null ? movement.EffectiveMoveSpeed : 0f;

    // ── 이동 소유권 ─────────────────────────────

    // 켜면 Enemy.Update가 이동·타겟 통지에서 손을 떼므로 노드가 정지와 전진을 직접 지시할 수 있다.
    // 켠 노드가 반드시 종료 시 반납한다 — 켜둔 채 중단되면 보스가 평타를 영구히 잃는다.
    public bool MovementOwned
    {
        get => enemy != null && enemy.MovementOwnedByBehavior;
        set
        {
            if (enemy == null)
            {
                return;
            }

            enemy.MovementOwnedByBehavior = value;
        }
    }

    // 소유권을 잡은 동안의 정지·재개 지시. 소유권이 없으면 Enemy.Update가 매 프레임 덮어쓴다.
    // 이동속도 배수를 0으로 내려도 하한 클램프에 걸려 멈추지 않으므로, 완전 정지는 이 축으로만 표현한다.
    public bool MovementStopped
    {
        get => movement != null && movement.IsStopped;
        set
        {
            if (movement == null)
            {
                return;
            }

            movement.IsStopped = value;
        }
    }

    // ── 받는 피해 ─────────────────────────────

    // 1=그대로, 0=무적, 1 초과=취약. 방어 태세 패턴이 감소치를 걸고 종료 시 1로 원복한다.
    public float DamageTakenFactor
    {
        get => enemy != null ? enemy.DamageTakenFactor : 1f;
        set
        {
            if (enemy == null)
            {
                return;
            }

            enemy.DamageTakenFactor = value;
        }
    }

    // ── 상태 조회 ─────────────────────────────

    // 진행 방향. 앞뒤 판정(EnemyUnitsInRangeCondition의 Direction)이 이 벡터와의 내적 부호를 본다.
    public Vector3 Forward => facing != null ? facing.forward : transform.forward;

    public float HpRatio => enemy != null ? enemy.HpRatio : 0f;

    // 반경 질의의 아군/적군 판정 기준. 노드를 플레이어 측 유닛에 붙여도 Ally/Hostile이 뒤집히지 않도록
    // 진영을 상수로 박지 않고 여기서 읽는다. Enemy가 없으면 보수적으로 Enemy 진영으로 본다.
    public Faction Faction => enemy != null ? enemy.Faction : Faction.Enemy;

    public LayerMask UnitLayerMask => unitLayerMask;

    // ── 애니메이션 ─────────────────────────────
    // 재생 종료 판정은 normalizedTime 폴링이다. AnimationEvent 방식은 클립마다 이벤트를 심어야 해서
    // 아직 존재하지 않는 보스 AnimatorController(#235)의 저작 부담을 노드 쪽으로 떠넘기게 된다.

    public bool HasAnimator => animator != null;

    public bool TryPlayAnimation(string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName))
        {
            return false;
        }

        animator.SetTrigger(triggerName);
        return true;
    }

    // 현재 상태의 재생 진행도(1=1회 재생 완료). 루프 클립이면 1을 넘어 계속 증가한다.
    // Animator가 없으면 1을 반환한다 — 대기 노드가 무한 Running으로 패턴을 붙잡지 않게.
    public float AnimationNormalizedTime => GetAnimationNormalizedTime(0);

    // 전이 중이면 참. 트리거 직후 몇 프레임은 아직 이전 상태가 읽히므로
    // 종료 판정에서 이 구간을 배제해야 준비 모션이 시작 전에 끝난 것으로 오판되지 않는다.
    public bool IsAnimatorInTransition => GetIsAnimatorInTransition(0);

    // 지속 상태(돌진 중 / 가드 중)는 Trigger가 아니라 Bool로 표현한다.
    //
    // Trigger는 전이가 소비하지 않으면 켜진 채로 남는다. 그래서 "해제 트리거"를 상태 밖에서
    // 쏘면 장전된 채 남아 있다가 다음번 진입을 즉시 취소한다 — 어디서든 안전하게 해제할 수
    // 없다는 뜻이고, 지속 상태에는 쓸 수 없다는 뜻이다. Bool은 멱등이라 기본 진군 브랜치가
    // 매 사이클 false로 덮어써도 무해하다(패턴 속도 배수 복귀와 같은 구조).
    //
    // 파라미터가 없으면 거짓을 반환한다. Animator.SetBool은 없는 이름에 대해 매 호출 경고를
    // 남기므로, 여기서 걸러 노드가 경고를 1회만 남기게 한다.
    public bool TrySetAnimatorBool(string parameterName, bool value)
    {
        if (!HasAnimatorBool(parameterName))
        {
            return false;
        }

        animator.SetBool(parameterName, value);
        return true;
    }

    public bool GetAnimatorBool(string parameterName) =>
        HasAnimatorBool(parameterName) && animator.GetBool(parameterName);

    private bool HasAnimatorBool(string parameterName)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == parameterName)
            {
                return true;
            }
        }

        return false;
    }

    // 레이어를 지정하는 형태. 상체 마스크 레이어(가드 / 봉인 / 소환)에서 재생되는 클립은
    // layer 0을 보면 안 된다 — 0번에서는 걷기가 루프 중이라 normalizedTime이 이미 1을 넘어 있고,
    // 재생 종료 대기가 시작하자마자 성공으로 빠져나간다.
    //
    // 범위를 벗어난 레이어는 "이미 끝났다"로 답한다. 대기 노드가 영구 Running으로
    // 패턴 전체를 붙잡는 것보다 한 번 어색하게 지나가는 편이 낫다.
    //
    // ⚠ 이 폴백만으로는 부족하다. 대기 노드는 "전이를 한 번 본 뒤"부터 진행도를 신뢰하는데,
    // 없는 레이어에서는 전이가 영영 관측되지 않아 폴백에 도달하지 못한다. 그래서 대기를
    // 시작하기 전에 HasAnimatorLayer로 걸러야 한다.
    public float GetAnimationNormalizedTime(int layer) =>
        HasAnimatorLayer(layer) ? animator.GetCurrentAnimatorStateInfo(layer).normalizedTime : 1f;

    public bool GetIsAnimatorInTransition(int layer) =>
        HasAnimatorLayer(layer) && animator.IsInTransition(layer);

    public bool HasAnimatorLayer(int layer) =>
        animator != null && layer >= 0 && layer < animator.layerCount;

    // ── 패턴 게이트(무상태 원칙의 유일한 예외) ─────────────────────────────

    public bool IsPatternReady(string key, float cooldownSeconds) =>
        patternMemory.IsReady(key, cooldownSeconds);

    public void MarkPatternUsed(string key) => patternMemory.MarkUsed(key);

    // ── 소환 ─────────────────────────────
    // 프리팹은 씬 참조를 들 수 없으므로 스포너를 스폰 시점에 주입받는다
    // (MonsterSpawn.SpawnPrefab — 경로를 주입하는 것과 같은 자리).
    // 정적 싱글톤을 쓰지 않는 이유: 스포너가 여러 개인 구성을 막지 않기 위해,
    // 소환체는 자기를 만든 스포너에 묶인다.

    public bool HasSpawner => spawner != null;

    public void BindSpawner(MonsterSpawn value)
    {
        spawner = value;
    }

    // 스폰 지점에 잡몹을 1체 투입한다. 스포너·프리팹이 없으면 null.
    // 소환체는 monsterParent 자식으로 들어가고 경로를 받는다 — 웨이브 클리어 판정에 포함되어야
    // "보스를 죽여야 물결이 멎는다"가 성립한다.
    public GameObject SpawnMinion(GameObject prefab)
    {
        if (spawner == null || prefab == null)
        {
            return null;
        }

        return spawner.SpawnMonster(prefab);
    }

    // 소환 상한(MaxAlive) 판정용. 보스 자신과 사망 연출 중인 몬스터도 포함된다
    // (MonsterSpawn.AliveMonsterCount 주석 참조).
    public int AliveMonsterCount => spawner != null ? spawner.AliveMonsterCount : 0;
}
