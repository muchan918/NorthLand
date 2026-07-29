namespace NorthLand.Combat
{
    // 타워 스탯 표시 문자열의 단일 출처(WL-079 해소).
    //
    // 예전에는 같은 라벨 조회와 같은 서식이 세 곳에 복제돼 있었다 — Tower.BuildStatsText,
    // AuraTower.BuildStatsText, TowerTooltipView.BuildStats. 표시 경로가 둘로 갈리는 것 자체는
    // 피할 수 없다(배치 **전** 툴팁은 인스턴스가 없어 SO만 보고 만들어야 한다), 그래서 갈라지는 지점을
    // "값을 어디서 얻는가"로만 좁히고 **라벨과 서식은 여기 한 곳**에 둔다.
    public static class TowerStatsFormatter
    {
        static string Label(string key) => LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, key);

        /// 공격 타워 3줄: 공격력 / 사거리 / 공격속도(=1÷간격).
        public static string BuildAttackLines(float damage, float range, float attackInterval)
        {
            float rate = attackInterval > 0f ? 1f / attackInterval : 0f;

            return $"{Label("game.tower.attack_damage")}: {damage:0.#}\n" +
                   $"{Label("game.tower.attack_range")}: {range:0.#}\n" +
                   $"{Label("game.tower.attack_speed")}: {rate:0.##}";
        }

        /// 오라 타워의 반경 한 줄(사거리 라벨을 공유한다 — 플레이어에게는 같은 "닿는 거리" 개념).
        public static string BuildRangeLine(float range)
            => $"{Label("game.tower.attack_range")}: {range:0.#}";

        /// 지속 피해 한 줄. 없으면 null.
        public static string BuildDotLine(OptionalDamage damage)
            => damage != null && damage.HasDamage
                ? $"DoT: {damage.DamageAmount:0.#} / {damage.TickInterval:0.#}s"
                : null;

        /// 감속 한 줄. 감속이 없으면(배율 1) null.
        public static string BuildSlowLine(float slowMultiplier)
            => slowMultiplier < 1f
                ? $"Slow: -{(1f - slowMultiplier) * 100f:0.#}%"
                : null;

        /// 버프 오라가 부여하는 스탯 변화 한 줄.
        public static string BuildModifierLine(StatModifier modifier)
        {
            if (modifier == null) return null;

            string sign = modifier.Amount >= 0 ? "+" : "";
            return $"{modifier.Stat} {sign}{modifier.Amount:0.#}{(modifier.IsPercentage ? "%" : "")}";
        }

        /// null·빈 줄을 건너뛰고 개행으로 잇는다. 전부 비면 null.
        public static string Join(params string[] lines)
        {
            string result = null;
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                result = result == null ? lines[i] : $"{result}\n{lines[i]}";
            }
            return result;
        }
    }
}
