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

        // 보스 BehaviorTree 패턴(속도 가감속)용 기준 이동속도.
        // Awake에서 Stat.MoveSpeed로 캐시하고, SetSpeedMultiplier가 이 값에 배수를 곱해 movement에 반영한다.
        float baseMoveSpeed;
        float speedMultiplier = 1f;

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

            // 자식까지 탐색(WL-089): MonsterMove가 자식 오브젝트에 붙는 프리팹에서도 movement를 찾도록
            // GetComponentInChildren 사용 — line 61·MonsterSpawn·MonsterStateMachine의 탐색 범위와 일치시킨다.
            movement = GetComponentInChildren<IMovementAgent>();

            baseMoveSpeed = Stat != null ? Stat.MoveSpeed : 0f;

            if (movement != null && Stat != null)
            {
                movement.SetMoveSpeed(baseMoveSpeed * speedMultiplier);
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
        public float SpeedMultiplier => speedMultiplier;

        // 이동속도 배수 설정(가감속 패턴). 기준 이동속도에 배수를 곱해 movement에 반영한다.
        // 음수 방지로 클램프. movement 미부착 시 배수만 저장되지만 재적용 지점이 없다(Awake의 movement 경고 참조).
        public void SetSpeedMultiplier(float multiplier)
        {
            speedMultiplier = Mathf.Max(0f, multiplier);
            movement?.SetMoveSpeed(baseMoveSpeed * speedMultiplier);
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
            currentHp -= info.Amount;
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
