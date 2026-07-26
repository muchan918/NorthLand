using System;
using UnityEngine;

namespace CombatSpace
{
    /// <summary>
    /// 버프 타일이 변경할 수 있는 타워 스탯.
    /// 새로운 타일 효과가 필요하면 여기에 스탯을 추가한다.
    /// </summary>
    public enum TileBuffStat
    {
        AttackRange,
        AttackDamage,
        AttackSpeed
    }

    /// <summary>
    /// 버프값을 계산하는 방식.
    /// Flat은 고정값, Percentage는 백분율 증가다.
    /// </summary>
    public enum TileModifierMode
    {
        Flat,
        Percentage
    }

    /// <summary>
    /// 같은 종류의 타일 효과가 여러 개 있을 때 적용할 중첩 규칙.
    /// 실제 규칙 선택은 나중에 중앙 설정에서 담당한다.
    /// </summary>
    public enum TileBuffStackMode
    {
        Sum,
        Max
    }

    /// <summary>
    /// 버프 타일 효과 하나를 표현하는 데이터.
    /// 예: 사거리 고정 +3, 공격력 +10%.
    /// </summary>
    [Serializable]
    public struct TileStatModifier
    {
        [Tooltip("변경할 타워 스탯")]
        public TileBuffStat Stat;

        [Tooltip("고정값 또는 백분율 적용 방식")]
        public TileModifierMode ModifierMode;

        [Min(0f)]
        [Tooltip("증가량. Percentage일 때 10은 10%를 의미한다.")]
        public float Value;
    }
}