using System.Collections.Generic;

namespace CombatSpace
{
    public sealed class TileBuffCalculator
    {
        public TileBuffCalculationResult Calculate(IReadOnlyList<BuffTileDefinition> definitions,TileBuffRuleSettings rules)
        {
            TileBuffCalculationResult result =new TileBuffCalculationResult();

            if (definitions == null || rules == null)
            {
                return result;
            }

            foreach (BuffTileDefinition definition in definitions)
            {
                if (definition == null)
                {
                    continue;
                }

                foreach (TileStatModifier modifier in definition.Modifiers)
                {
                    float current =result.GetValue(modifier.Stat,modifier.ModifierMode);

                    TileBuffStackMode stackMode =rules.GetStackMode(modifier.Stat,modifier.ModifierMode);

                    float calculated = stackMode switch
                    {
                        TileBuffStackMode.Sum =>current + modifier.Value,

                        TileBuffStackMode.Max =>current > modifier.Value? current: modifier.Value,

                        _ => current
                    };

                    result.SetValue(modifier.Stat,modifier.ModifierMode,calculated);
                }
            }

            return result;
        }
    }
}