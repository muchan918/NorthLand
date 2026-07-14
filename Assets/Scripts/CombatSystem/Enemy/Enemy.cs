using UnityEngine;

namespace NorthLand.Combat
{
    public class Enemy : MonoBehaviour, IAttacker, IDamageable
    {
        [SerializeField] EnemyData data;

        // TODO(TBD): 대상 탐지 필터링을 LayerMask로 할지 Tag로 할지 미확정.
        //            현재는 임시로 LayerMask 방식 사용. 팀 컨벤션 회의 후 결정 및 수정 예정.
        [SerializeField] LayerMask targetLayerMask;   // 아군 유닛 + 본진 레이어

        float currentHp;
        float cooldownTimer;
        bool isDying;

        // 타겟 탐색용 재사용 버퍼. 매 프레임 힙 할당을 피하기 위해 사용(최대 16개 감지).
        readonly Collider[] hitBuffer = new Collider[16];

        void Awake()
        {
            currentHp = data.maxHp;
        }

        public Faction Faction => Faction.Enemy;
        public bool IsDead => currentHp <= 0f;

        public float AttackDamage => data.attackDamage;
        public float AttackRange => data.attackRange;
        public float AttackInterval => data.attackInterval;

        void Update()
        {
            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer > 0f) return;

            var target = FindTarget();
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
            target.TakeDamage(new DamageInfo(AttackDamage, this));
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
