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

        // 이동 액추에이터(선택적). 대상이 사거리에 들면 멈추도록 이 컴포넌트가 구동한다.
        // 구체 타입이 아니라 계약(IMovementAgent)에 의존 — 이동 구현에 결합하지 않는다.
        IMovementAgent movement;

        // 타겟 탐색용 재사용 버퍼. 매 프레임 힙 할당을 피하기 위해 사용(최대 16개 감지).
        readonly Collider[] hitBuffer = new Collider[16];

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
            currentHp = Stat != null ? Stat.MaxHp : 0f;
            movement = GetComponent<IMovementAgent>();
        }

        public Faction Faction => Faction.Enemy;
        public bool IsDead => currentHp <= 0f;

        // Stat 미설정(Stat==null)에서도 안전하도록 null 가드(공개 IAttacker 계약).
        public float AttackDamage => Stat != null ? Stat.AttackDamage : 0f;
        public float AttackRange => Stat != null ? Stat.AttackRange : 0f;
        public float AttackInterval => Stat != null ? Stat.AttackInterval : 0f;

        void Update()
        {
            // 전투 스탯이 없는(미설정) 개체는 동작하지 않음
            if (Stat == null) return;

            // 대상 탐색은 매 프레임(쿨다운과 무관) — 이동 제어에 최신 상태가 필요하기 때문.
            var target = FindTarget();

            // 사거리 안에 대상(본진/아군 유닛)이 있으면 멈춰서 공격, 없으면 전진.
            // MonsterMove 등 IMovementAgent 구현체를 NavMeshAgent처럼 구동한다.
            if (movement != null)
                movement.IsStopped = target != null;

            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer > 0f) return;

            if (target != null && TryAttack(target))
                cooldownTimer = AttackInterval;
        }

        public void TakeDamage(DamageInfo info)
        {
            currentHp -= info.Amount;
            // Debug.Log($"{name} took {info.Amount} dmg, hp={currentHp}");

            if (IsDead)
                Die();
        }

        // 사망 처리. 추후 오브젝트 풀링 도입 시 이 메서드 내부만 "풀 반환"으로 교체하면 된다.
        void Die()
        {
            if (isDying) return;   // 같은 프레임 다중 타격에 의한 이중 사망 처리 방지
            isDying = true;
            Destroy(gameObject);
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;

            // Ranged는 투사체 발사, 그 외(Melee/Boss)는 근접 즉시 데미지.
            // (Boss의 BehaviorTree 기반 AI는 미착수 — WL-012. 현재는 근접 공격으로 임시 처리)
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
