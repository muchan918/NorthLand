using System;
using NorthLand.Core;
using UnityEngine;

namespace NorthLand.Combat
{
    public class Enemy : MonoBehaviour, IAttacker, IDamageable
    {
        [SerializeField] EnemyAsset data;

        // TODO(TBD): 대상 탐지 필터링을 LayerMask로 할지 Tag로 할지 미확정.
        //            현재는 임시로 LayerMask 방식 사용. 팀 컨벤션 회의 후 결정 및 수정 예정.
        [SerializeField] LayerMask targetLayerMask;   // 아군 유닛 + 본진 레이어

        float currentHp;
        float cooldownTimer;
        bool isDying;

        // 받는 피해 배수(#233). 1=그대로, 0=무적, 1 초과=취약. 방어 태세 패턴이 감소치를 건다.
        // 이 값을 Enemy가 소유하는 이유: 피해 적용 지점이 TakeDamage 하나뿐이라
        // EnemyAgent가 같은 값을 들면 동기화가 깨진다(EnemyAgent는 전달만 한다).
        float damageTakenFactor = 1f;

        // 이동 액추에이터(선택적). 대상이 사거리에 들면 멈추도록 이 컴포넌트가 구동한다.
        // 구체 타입이 아니라 계약(IMovementAgent)에 의존 — 이동 구현에 결합하지 않는다.
        IMovementAgent movement;

        // 타겟 탐색용 재사용 버퍼. 매 프레임 힙 할당을 피하기 위해 사용(최대 16개 감지).
        readonly Collider[] hitBuffer = new Collider[16];

        private MonsterStateMachine monsterStateMachine;

        private MonsterMove monsterMove;

        // 보스 BehaviorTree 실행 주체(보스가 아니면 null). 정지 핸들 확보용 필드 —
        // 게임 종료·사망 시 이 에이전트를 꺼서 그래프 틱을 멈춘다.
        private Unity.Behavior.BehaviorGraphAgent behaviorAgent;

        // EnemyType에 맞는 공통 전투 스탯 해석. data 미할당 시 null.
        EnemyAsset.CombatFields Stat => data == null ? null : data.EnemyType switch
        {
            EnemyType.Melee  => data.Melee.Stat,
            EnemyType.Ranged => data.Ranged.Stat,
            EnemyType.Boss   => data.Boss.Stat,
            _ => null,
        };

        void Awake()
        {
            monsterStateMachine = GetComponent<MonsterStateMachine>();

            currentHp = Stat != null ? Stat.MaxHp : 0f;

            // 자식까지 탐색(WL-093): MonsterMove가 자식 오브젝트에 붙는 프리팹에서도 movement를 찾도록
            // GetComponentInChildren 사용 — line 61·MonsterSpawn·MonsterStateMachine의 탐색 범위와 일치시킨다.
            movement = GetComponentInChildren<IMovementAgent>();

            // 기준 속도만 주입한다. 패턴 배수·디버프 배수는 movement가 축별로 소유하고 합성한다(#233).
            if (movement != null && Stat != null)
            {
                movement.SetMoveSpeed(Stat.MoveSpeed);
            }
            else if (movement == null)
            {
                // 이동 액추에이터를 못 찾으면 보스 속도 가감속 패턴이 조용히 무동작하므로 경고로 드러낸다.
                Debug.LogWarning($"[{name}] IMovementAgent를 찾지 못해 이동속도 패턴이 동작하지 않습니다.", this);
            }

            OnHpChanged?.Invoke(currentHp, MaxHp);

            monsterMove = GetComponentInChildren<MonsterMove>();

            if (monsterMove != null)
            {
                monsterMove.RouteCompleted += HandleRouteCompleted;
            }

            // 보스 데이터 주도 AI: EnemyAsset.Boss.BehaviorTree에 그래프가 지정돼 있으면
            // BehaviorGraphAgent를 확보(없으면 부착)해 그래프를 주입한다. 그래프 실행 주체는 에이전트지만,
            // "어떤 보스가 어떤 그래프를 쓰는지"는 프리팹 배선이 아니라 SO(tracked)가 단일 출처로 소유한다.
            if (data != null && data.EnemyType == EnemyType.Boss && data.Boss != null && data.Boss.BehaviorTree != null)
            {
                behaviorAgent = GetComponent<Unity.Behavior.BehaviorGraphAgent>();
                if (behaviorAgent == null)
                {
                    behaviorAgent = gameObject.AddComponent<Unity.Behavior.BehaviorGraphAgent>();
                }

                behaviorAgent.Graph = data.Boss.BehaviorTree;
            }
        }

        public Faction Faction => Faction.Enemy;
        public bool IsDead => currentHp <= 0f;

        // HP UI(월드 스페이스 체력바 등)가 구독하는 공개 계약. Awake와 TakeDamage에서 통지.
        public float CurrentHp => currentHp;
        public float MaxHp => Stat != null ? Stat.MaxHp : 0f;
        public event Action<float, float> OnHpChanged;

        // Stat 미설정(Stat==null)에서도 안전하도록 null 가드(공개 IAttacker 계약).
        public float AttackDamage => Stat != null ? Stat.AttackDamage : 0f;
        public float AttackRange => Stat != null ? Stat.AttackRange : 0f;
        public float AttackInterval => Stat != null ? Stat.AttackInterval : 0f;

        // ── 보스 BehaviorTree 패턴 훅(#193) ─────────────────────────────
        // 커스텀 BT 노드(CombatSystem/Enemy/Boss)가 호출하는 공개 계약.
        // 숫자(임계값·회복량·배수)는 그래프 노드 입력으로 authoring하고, 여기선 상태 변경만 담당한다.

        // 현재 체력 비율(0~1). "HP 30% 이하" 같은 조건 노드가 참조한다. MaxHp==0이면 0.
        public float HpRatio => MaxHp > 0f ? currentHp / MaxHp : 0f;

        // 체력 회복. MaxHp를 넘지 않도록 클램프하고, 이미 죽었으면 무시. HP UI에 변경을 통지한다.
        public void Heal(float amount)
        {
            if (isDying || amount <= 0f || Stat == null)
            {
                return;
            }

            currentHp = Mathf.Min(currentHp + amount, MaxHp);
            OnHpChanged?.Invoke(currentHp, MaxHp);
        }

        // 현재 이동속도 배수(가감속 램프 노드가 보간 시작값으로 읽는다).
        // #233 이후 값의 소유자는 movement의 패턴 축이다 — 여기서 별도로 들지 않는다.
        public float SpeedMultiplier => movement != null ? movement.PatternSpeedFactor : 1f;

        // 이동속도 배수 설정(가감속 패턴). #233 이후 movement의 패턴 축에 위임한다 —
        // 감속 타워의 디버프 축과 곱해져야 서로를 지우지 않으므로 여기서 최종 속도를 계산하지 않는다.
        // 중간보스 그래프(MidBossBehavior.asset)가 이 진입점을 쓰고 있어 남겨둔다.
        // 신규 노드는 EnemyAgent.PatternSpeedFactor를 경유한다.
        public void SetSpeedMultiplier(float multiplier)
        {
            if (movement == null)
            {
                return;
            }

            movement.PatternSpeedFactor = multiplier;
        }

        // ── 보스 BT 진입점(#233) ─────────────────────────────

        // BT 이동 소유권. 켜져 있는 동안 Update가 이동·타겟 통지를 건드리지 않으므로
        // 노드가 정지와 전진을 직접 지시할 수 있다(P1 준비 동작 중 정지, 돌진 중 전진 유지).
        //
        // IsStopped 뿐 아니라 타겟 통지까지 함께 다루는 이유: MonsterStateMachine이
        // Attack 상태에서 SetMoveEnabled(false)를 걸기 때문에(MonsterStateMachine.cs:141),
        // 타겟 통지를 살려두면 돌진이 본진 사거리에 진입하는 순간 멈춰 P1이 절름발이가 된다.
        //
        // 통지를 "막는 것"만으로는 부족하다. MonsterMove에는 IsStopped(:147)와 canMove(:159)
        // 두 개의 독립된 게이트가 있고 노드는 IsStopped만 만진다. 소유권 획득 전에 이미
        // hasTarget=true(Attack 상태, canMove=false)였다면 통지만 막아도 그 상태가 그대로 latch되어
        // 돌진 노드가 IsStopped=false를 매 프레임 써도 보스가 제자리에서 배수만 올린다.
        // 그래서 Update가 소유권 브랜치에서 SetHasTarget(false)로 상태를 내려준다.
        //
        // 부수 효과로 소유권 중에는 근접 평타가 나가지 않는다 — 충돌 피해가 그 역할을 대신한다.
        public bool MovementOwnedByBehavior { get; set; }

        // 받는 피해 배수. 0 미만은 클램프한다(0=무적). 상한은 두지 않아 취약 디버프로도 쓸 수 있다.
        public float DamageTakenFactor
        {
            get => damageTakenFactor;
            set => damageTakenFactor = Mathf.Max(0f, value);
        }

        void Update()
        {
            if (Stat == null || isDying)
            {
                return;
            }

            GameManager gameManager = GameManager.Instance;

            if (gameManager != null &&gameManager.Result != GameResult.Playing)
            {
                if (movement != null)
                {
                    movement.IsStopped = true;
                }

                // 게임 종료(승리/패배) 후 보스 BT가 계속 tick하지 않도록 에이전트를 끈다.
                if (behaviorAgent != null)
                {
                    behaviorAgent.enabled = false;
                }

                monsterStateMachine?.SetHasTarget(false);
                return;
            }

            // BT가 이동을 소유한 동안은 이동·공격에서 손을 뗀다(MovementOwnedByBehavior 주석 참조).
            //
            // 타겟 없음을 한 번 내려주는 것이 핵심이다. 소유권 진입 전에 남아 있던 Attack 상태를
            // 풀지 않으면 MonsterMove.canMove가 false로 고착돼 돌진 노드가 보스를 움직일 수 없다.
            // ChangeState가 동일 상태를 걸러내므로 매 프레임 호출해도 무해하다.
            //
            // 쿨다운은 계속 흘려보낸다 — 소유권 반납 직후 인위적인 공격 지연이 생기지 않게.
            if (MovementOwnedByBehavior)
            {
                monsterStateMachine?.SetHasTarget(false);
                cooldownTimer -= Time.deltaTime;
                return;
            }

            IDamageable target = FindTarget();
            bool hasTarget = target != null;

            if (movement != null)
            {
                movement.IsStopped = hasTarget;
            }

            monsterStateMachine?.SetHasTarget(hasTarget);

            cooldownTimer -= Time.deltaTime;

            if (!hasTarget || cooldownTimer > 0f)
            {
                return;
            }

            if (TryAttack(target))
            {
                cooldownTimer = AttackInterval;
            }
        }


        public void TakeDamage(DamageInfo info)
        {
            // 받는 피해 배수를 여기 한 곳에서만 적용한다(#233) — 방어 태세 패턴의 감쇠 지점.
            currentHp -= info.Amount * damageTakenFactor;
            // Debug.Log($"{name} took {info.Amount} dmg, hp={currentHp}");
            OnHpChanged?.Invoke(currentHp, MaxHp);

            if (IsDead)
            {
                Die();
            }
        }

        // 같은 프레임 다중 타격에 의한 이중 사망 처리 방지
        // 사망 처리. 추후 오브젝트 풀링 도입 시 이 메서드 내부만 "풀 반환"으로 교체하면 된다.
        void Die()
        {
            if (isDying)
            {
                return;
            }

            isDying = true;

            // 사망 연출 지연 동안(파괴 전까지) 보스 BT가 계속 돌지 않도록 에이전트를 끈다.
            if (behaviorAgent != null)
            {
                behaviorAgent.enabled = false;
            }

            if (monsterStateMachine != null)
            {
                monsterStateMachine.ChangeState(MonsterState.Death);
            }
            else
            {
                Debug.LogWarning($"[{name}] MonsterStateMachine이 없어 사망 애니메이션 없이 즉시 제거합니다.",this);

                Destroy(gameObject);
            }
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;

            // Ranged는 투사체 발사, 그 외(Melee/Boss)는 근접 즉시 데미지.
            // (#193: Boss의 BehaviorTree는 속도 가감속·HP 회복 등 상위 패턴을 담당하고,
            //  공격 자체는 이 근접 경로를 그대로 사용한다. 원거리 보스가 필요해지면 확장.)
            if (data.EnemyType == EnemyType.Ranged)
                return TryRangedAttack(target);

            target.TakeDamage(new DamageInfo(AttackDamage, this));
            return true;
        }

        // 원거리 공격: Tower와 동일한 Projectile을 단일 명중(Single)으로 발사한다.
        bool TryRangedAttack(IDamageable target)
        {
            var ranged = data.Ranged;
            if (ranged.ProjectilePrefab == null) return false;

            var obj = Instantiate(ranged.ProjectilePrefab, transform.position, Quaternion.identity);
            if (!obj.TryGetComponent<Projectile>(out var projectile))
            {
                Destroy(obj);   // Projectile 컴포넌트가 없으면 스폰물을 제거하고 공격 실패 처리
                return false;
            }

            projectile.Init(target, AttackDamage, ranged.ProjectileSpeed, this, ProjectileImpact.MakeSingle());
            return true;
        }

        private void OnDestroy()
        {
            if (monsterMove != null)
            {
                monsterMove.RouteCompleted -= HandleRouteCompleted;
            }
        }
        private void HandleRouteCompleted()
        {
            if (isDying)
            {
                return;
            }

            Destroy(gameObject);
        }

        // 사거리 내에서 가장 가까운 아군 대상(유닛/본진)을 타겟으로 선정
        IDamageable FindTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, AttackRange, hitBuffer, targetLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                var damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable != null
                    && damageable.Faction != Faction
                    && !damageable.IsDead)
                {
                    float sqrDistance = (hit.transform.position - transform.position).sqrMagnitude;
                    if (sqrDistance < closestSqrDistance)
                    {
                        closestSqrDistance = sqrDistance;
                        closest = damageable;
                    }
                }
            }

            return closest;
        }
    }
}
