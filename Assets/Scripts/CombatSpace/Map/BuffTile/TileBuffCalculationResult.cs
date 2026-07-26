using System.Collections.Generic;

namespace CombatSpace
{
    public sealed class TileBuffCalculationResult
    {
        private readonly Dictionary<(TileBuffStat Stat, TileModifierMode Mode),float> values =new Dictionary<(TileBuffStat, TileModifierMode),float>();

        public float GetValue(TileBuffStat stat,TileModifierMode mode)
        {
            return values.TryGetValue((stat, mode),out float value)? value: 0f;
        }

        internal void SetValue(TileBuffStat stat,TileModifierMode mode,float value)
        {
            values[(stat, mode)] = value;
        }
    }
}