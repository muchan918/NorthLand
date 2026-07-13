using UnityEngine;

namespace NorthLand.Combat
{
    public class Tower : MonoBehaviour, IAttacker
    {
        [SerializeField] TowerData data;
        [SerializeField] LayerMask enemyLayerMask;

        float cooldownTimer;

        public Faction Faction => Faction.Player;
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

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;
            target.TakeDamage(new DamageInfo(AttackDamage, this));
            return true;
        }

        IDamageable FindTarget()
        {
            var hits = Physics.OverlapSphere(transform.position, AttackRange, enemyLayerMask);
            foreach (var hit in hits)
            {
                if (hit.TryGetComponent<IDamageable>(out var damageable)
                    && damageable.Faction != Faction
                    && !damageable.IsDead)
                {
                    return damageable;
                }
            }
            return null;
        }
    }
}
