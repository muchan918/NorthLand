namespace NorthLand.Combat
{
    public interface IDamageable
    {
        Faction Faction { get; }
        bool IsDead { get; }

        void TakeDamage(DamageInfo info);
    }
}
