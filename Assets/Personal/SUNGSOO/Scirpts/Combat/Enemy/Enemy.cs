using UnityEngine;

namespace NorthLand.Combat
{
    public class Enemy : MonoBehaviour, IAttacker, IDamageable
    {
        [SerializeField] EnemyData data;

        float currentHp;

        void Awake()
        {
            currentHp = data.maxHp;
        }

        public Faction Faction => Faction.Enemy;
        public bool IsDead => currentHp <= 0f;

        public float AttackDamage => data.attackDamage;
        public float AttackRange => data.attackRange;
        public float AttackInterval => data.attackInterval;

        public void TakeDamage(DamageInfo info)
        {
            currentHp -= info.Amount;
            Debug.Log($"{name} took {info.Amount} dmg, hp={currentHp}");
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;
            target.TakeDamage(new DamageInfo(AttackDamage, this));
            return true;
        }
    }
}
