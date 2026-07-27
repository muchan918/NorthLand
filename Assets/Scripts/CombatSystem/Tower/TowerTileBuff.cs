using CombatSpace;
using UnityEngine;

namespace NorthLand.Combat
{
    public sealed class TowerTileBuff : MonoBehaviour
    {
        public TileBuffCalculationResult Result { get; private set; }
            = new TileBuffCalculationResult();

        public void Initialize(TileBuffCalculationResult result)
        {
            Result = result ?? new TileBuffCalculationResult();
        }

        public float GetValue(
            TileBuffStat stat,
            TileModifierMode mode)
        {
            return Result.GetValue(stat, mode);
        }
    }
}