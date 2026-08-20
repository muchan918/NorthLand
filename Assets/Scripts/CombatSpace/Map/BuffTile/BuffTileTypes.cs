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


        // 공격속도는 Percentage 모드만 지원한다.
        // 값이 증가할수록 AttackInterval이 짧아진다.
        AttackSpeed,

        // 오라(장판) 반경. AttackRange와 **별도 축**이다 — 타일 버프는 증가량을 칸 단위 절댓값으로
        // 약속하는데(TileSize 6), 오라 기본 반경은 1.6~2.7칸이라 공격 사거리(3칸)보다 작다.
        // 같은 축에 두면 같은 "+3칸"이 오라에서는 지름 3배가 되고, 장판은 상시 보이므로 그대로 드러났다.
        //
        // 새 항목은 **뒤에만** 추가한다 — 값이 SO에 int로 직렬화돼 있어 중간 삽입은 기존 저작을 조용히 밀어낸다.
        AuraRadius
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