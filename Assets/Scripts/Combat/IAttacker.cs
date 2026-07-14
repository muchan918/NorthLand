namespace NorthLand.Combat
{
    public interface IAttacker
    {
        Faction Faction { get; }
        float AttackDamage { get; }
        float AttackRange { get; }
        float AttackInterval { get; }

        bool TryAttack(IDamageable target);
    }
}
