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
        enemy = GetComponent<Enemy>();

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
    public float AnimationNormalizedTime =>
        animator != null ? animator.GetCurrentAnimatorStateInfo(0).normalizedTime : 1f;

    // 전이 중이면 참. 트리거 직후 몇 프레임은 아직 이전 상태가 읽히므로
    // 종료 판정에서 이 구간을 배제해야 준비 모션이 시작 전에 끝난 것으로 오판되지 않는다.
    public bool IsAnimatorInTransition => animator != null && animator.IsInTransition(0);

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
