using System.Collections.Generic;
using CombatSpace;
using UnityEngine;

public sealed class TowerTileBuff : MonoBehaviour
{
    private readonly TileBuffCalculator calculator =new TileBuffCalculator();

    private readonly List<BuffTileDefinition> definitions =new List<BuffTileDefinition>();

    public TileBuffCalculationResult Result{get;private set;} = new TileBuffCalculationResult();

    public void Initialize(IReadOnlyList<BattleTile> tiles,TileBuffRuleSettings rules)
    {
        definitions.Clear();

        if (tiles != null)
        {
            foreach (BattleTile tile in tiles)
            {
                if (tile == null ||tile.BuffDefinition == null)
                {
                    continue;
                }

                definitions.Add(tile.BuffDefinition);
            }
        }

        Result =calculator.Calculate(definitions,rules);
    }

    public float GetValue(TileBuffStat stat,TileModifierMode mode)
    {
        return Result.GetValue(stat,mode);
    }
}