
using CombatSpace;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 버프 타일의 중첩 규칙을 중앙에서 관리한다.
/// 개별 타일은 효과와 값만 가지고, 중첩 방식은 이 설정을 따른다.
/// </summary>
[CreateAssetMenu(fileName = "TileBuffRuleSettings", menuName = "Combat Space/Tile Buff Rule Settings")]
public sealed class TileBuffRuleSettings : ScriptableObject
{
    [SerializeField]
    private List<TileBuffStackRule> rules = new List<TileBuffStackRule>();

    public IReadOnlyList<TileBuffStackRule> Rules => rules;

    /// <summary>
    /// 해당 스탯의 중첩 방식을 반환한다.
    /// 규칙이 없으면 안전하게 Max를 사용한다.
    /// </summary>
    public TileBuffStackMode GetStackMode(TileBuffStat stat, TileModifierMode modifierMode)
    {
        foreach (TileBuffStackRule rule in rules)
        {
            if (rule == null)
            {
                continue;
            }

            if (rule.Stat == stat && rule.ModifierMode == modifierMode)
            {
                return rule.StackMode;
            }
        }

        Debug.LogWarning($"버프 타일 중첩 규칙이 없습니다. Stat={stat}, Mode={modifierMode}.{TileBuffStackMode.Max}를 사용합니다.", this);

        return TileBuffStackMode.Max;
    }

    /// <summary>
    /// 누락되거나 중복된 규칙이 있는지 검사한다.
    /// </summary>
    public bool Validate(
        out string errorMessage)
    {
        if (rules == null || rules.Count == 0)
        {
            errorMessage = "버프 타일 중첩 규칙이 없습니다.";

            return false;
        }

        var keys = new HashSet<(TileBuffStat Stat, TileModifierMode Mode)>();

        foreach (TileBuffStackRule rule in rules)
        {
            if (rule == null)
            {
                errorMessage = "비어 있는 버프 타일 중첩 규칙이 있습니다.";

                return false;
            }

            var key = (rule.Stat, rule.ModifierMode);

            if (!keys.Add(key))
            {
                errorMessage = $"중복된 버프 타일 중첩 규칙이 있습니다.Stat={rule.Stat},Mode={rule.ModifierMode}";

                return false;
            }
        }

        errorMessage = null;

        return true;
    }
}