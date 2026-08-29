using NorthLand.Combat;

namespace NorthLand.UI
{
    public enum TowerCategory
    {
        Single,
        Area,
        Aura
    }

    public static class TowerCategoryResolver
    {
        public static TowerCategory Of(TowerAsset tower)
        {
            if (tower.HasAction<BuffAuraAction>() ||
                tower.HasAction<DebuffAuraAction>())
            {
                return TowerCategory.Aura;
            }

            if (tower.HasAction<BeamAction>())
            {
                return tower.Beam.MaxTargets > 1
                    ? TowerCategory.Area
                    : TowerCategory.Single;
            }

            return tower.Impact == ImpactKind.Single
                ? TowerCategory.Single
                : TowerCategory.Area;
        }
    }
}