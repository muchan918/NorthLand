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

        /// 지속 피해 한 줄. 피해가 없으면 null.
        /// **SO 원본이 아니라 실효값(원장 합성 후)을 넘길 것** — 패널 표기와 실제 효과가 어긋나면
        /// WL-079/WL-130이 지적한 "표시부와 적용부가 규칙을 각자 쓰는" 문제가 재발한다.
        public static string BuildDotLine(float damagePerTick, float tickInterval)
            => damagePerTick > 0f && tickInterval > 0f
                ? $"DoT: {damagePerTick:0.#} / {tickInterval:0.#}s"
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

        /// 합성으로 물려받는 효과 한 줄(#274 Phase 5). 계승분이 없으면 null.
        /// 효과 이름은 DoT/Slow/Stun 줄과 같은 표기를 쓴다 — 툴팁에서 "물려받는다"고 본 이름과
        /// 배치 후 정보 패널에 뜨는 이름이 다르면 플레이어가 같은 것으로 인식하지 못한다.
        public static string BuildInheritLine(System.Collections.Generic.IEnumerable<EffectKind> kinds)
        {
            if (kinds == null) return null;

            string joined = null;
            foreach (EffectKind kind in kinds)
            {
                string name = EffectName(kind);
                joined = joined == null ? name : $"{joined} + {name}";
            }

            return joined == null ? null : $"Inherit: {joined}";
        }

        /// 효과 종류의 표시명.
        /// ⚠ 로컬라이즈 키를 새로 만들지 않는다 — `LocalizationHelper.Get`은 키가 없으면 빈 문자열이
        /// 아니라 에러를 내므로, 키를 먼저 등록하지 않은 채 조회하면 콘솔이 더럽혀진다.
        /// DoT/Slow/Stun 줄이 이미 하드코딩 라벨을 쓰고 있어 표기 일관성도 이쪽이 맞다.
        /// 로컬라이즈가 필요해지면 이 함수 하나만 바꾸면 된다.
        public static string EffectName(EffectKind kind) => kind.ToString();

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
