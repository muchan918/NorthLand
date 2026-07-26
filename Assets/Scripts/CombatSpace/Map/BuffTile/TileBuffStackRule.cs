using System;
using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    /// <summary>
    /// 특정 스탯과 계산 방식에 적용할 중첩 규칙.
    /// </summary>
    [Serializable]
    public sealed class TileBuffStackRule
    {
        [SerializeField]
        private TileBuffStat stat;

        [SerializeField]
        private TileModifierMode modifierMode;

        [SerializeField]
        private TileBuffStackMode stackMode;

        public TileBuffStat Stat => stat;

        public TileModifierMode ModifierMode =>modifierMode;

        public TileBuffStackMode StackMode =>stackMode;
    }

}