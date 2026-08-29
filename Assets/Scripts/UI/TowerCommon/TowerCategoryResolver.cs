using System;
using NorthLand.Combat;

namespace NorthLand.UI
{
    [Flags]
    public enum TowerCategory
    {
        None = 0,
        Single = 1 << 0,
        Area = 1 << 1,
        Aura = 1 << 2
    }

    public static class TowerCategoryResolver
    {
        public static TowerCategory Of(TowerAsset tower)
        {
            if (tower == null)
                return TowerCategory.None;

            TowerCategory category = TowerCategory.None;

            if (tower.HasAction<BuffAuraAction>() ||
                tower.HasAction<DebuffAuraAction>())
            {
                category |= TowerCategory.Aura;
            }

            if (tower.HasAction<AttackAction>())
            {
                category |= tower.Impact == ImpactKind.Single
                    ? TowerCategory.Single
                    : TowerCategory.Area;
            }

            if (tower.HasAction<BeamAction>())
            {
                category |= tower.Beam.MaxTargets > 1
                    ? TowerCategory.Area
                    : TowerCategory.Single;
            }

            return category;
        }
    }
}