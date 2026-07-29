using UnityEngine;

namespace NorthLand.Combat
{
    public interface IDamageable
    {
        public Transform HitPosition { get; } 
        Faction Faction { get; }
        bool IsDead { get; }

        void TakeDamage(DamageInfo info);
    }
}
