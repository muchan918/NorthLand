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

        /// 해금 웨이브 한 줄(#504) — 아직 잠긴 타워의 툴팁에만 붙는다.
        /// **해금 여부는 여기서 판단하지 않는다**(<see cref="TowerAsset.IsUnlocked"/>가 정본) —
        /// 이 클래스는 라벨과 서식만 소유한다. 호출부가 잠긴 타워일 때만 이 줄을 요청한다.
        public static string BuildUnlockWaveLine(int unlockWave)
            => $"{Label("game.tower.unlock_wave")}: {unlockWave}";

        /// 지속 피해 한 줄. 피해가 없으면 null.
        /// **SO 원본이 아니라 실효값(원장 합성 후)을 넘길 것** — 패널 표기와 실제 효과가 어긋나면
        /// WL-079/WL-130이 지적한 "표시부와 적용부가 규칙을 각자 쓰는" 문제가 재발한다.
        public static string BuildDotLine(float damagePerTick, float tickInterval)
            => damagePerTick > 0f && tickInterval > 0f
                ? $"DoT: {damagePerTick:0.#} / {tickInterval:0.#}s"
                : null;

        /// 다중 타겟 지속딜(빔) 한 줄 — 대상 1기당 DPS와 동시 타격 대상 수(#298).
        public static string BuildBeamLine(float dps, int maxTargets)
            => dps > 0f && maxTargets > 0
                ? $"DPS: {dps:0.#} × {maxTargets}"
                : null;

        /// 성장(램프업) 한 줄 — 현재 스택/상한과 그 배율(#300).
        ///
        /// ⚠ 정보 패널은 **선택 시점 스냅샷**이라 이 줄은 실시간으로 갱신되지 않는다. 그래서 현재
        /// 값만 쓰지 않고 상한을 함께 낸다 — 플레이어가 "이 타워가 어디까지 자라는가"를 알 수 있어야
        /// 한 장면의 숫자가 전부인 것으로 오해하지 않는다.
        /// 둘째 축(`secondaryStat`/`secondaryMultiplier`)은 배율이 1이면 생략한다 — 단일 축 타워의
        /// 표기를 바꾸지 않기 위해서다. 축이 둘일 때 스택은 공유하므로 한 번만 낸다.
        public static string BuildRampLine(TowerStat stat, int stacks, int maxStacks, float multiplier,
                                           TowerStat secondaryStat = default, float secondaryMultiplier = 1f)
        {
            if (maxStacks <= 0) return null;

            string line = $"Ramp({stat}): {stacks}/{maxStacks} ×{multiplier:0.##}";

            return secondaryMultiplier != 1f
                ? $"{line} · {secondaryStat} ×{secondaryMultiplier:0.##}"
                : line;
        }

        /// 조준 방식의 표시명(#387). 정책은 키만 알고, 로케일 해석은 여기서 한다.
        ///
        /// ⚠ 이 값은 **조회 시점 스냅샷**이라 로케일이 바뀌어도 자동 갱신되지 않는다
        /// (`LocalizationHelper` 주석의 pull 경로 한계 — 정보 패널 전체가 같은 트레이드오프다, #153).
        /// 패널을 다시 열면 새 로케일로 해석된다.
        public static string TargetingName(TargetingPolicy policy)
            => policy == null ? string.Empty : Label(policy.DisplayNameKey);

        /// 연발 한 줄(#336). 1발이면 null — 대부분의 타워에서 줄이 늘지 않는다.
        public static string BuildBurstLine(int burstCount)
            => burstCount > 1 ? $"Burst: ×{burstCount}" : null;

        /// 착탄 지속 구역 한 줄(#336) — 반경과 남는 시간. 구역이 없으면 null.
        /// 구역이 **거는 효과**는 DoT 줄이 따로 낸다(같은 `Effects`를 쓰므로 중복 표기하지 않는다).
        public static string BuildGroundZoneLine(float radius, float duration)
            => radius > 0f && duration > 0f
                ? $"Zone: {radius:0.#} / {duration:0.#}s"
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

        /// 합성으로 물려받는 효과 한 줄(#274 Phase 5).
        /// 효과 이름은 DoT/Slow/Stun 줄과 같은 표기를 쓴다 — 툴팁에서 "물려받는다"고 본 이름과
        /// 배치 후 정보 패널에 뜨는 이름이 다르면 플레이어가 같은 것으로 인식하지 못한다.
        ///
        /// ⚠ **null과 빈 집합을 다르게 표시한다**(`ResolveInheritedKinds`와 같은 구분):
        ///   null    계승 개념이 없는 레시피 → 줄 자체를 안 낸다
        ///   빈 집합  계승은 켰는데 물려줄 게 없다 → **"Inherit: 없음"**을 낸다
        /// 빈 집합에 줄을 안 내면 "표시 0인데 실제로는 전부 off"가 되어 또 어긋난다.
        public static string BuildInheritLine(System.Collections.Generic.IEnumerable<EffectKind> kinds)
        {
            if (kinds == null) return null;

            string joined = null;
            foreach (EffectKind kind in kinds)
            {
                string name = EffectName(kind);
                joined = joined == null ? name : $"{joined} + {name}";
            }

            return $"Inherit: {joined ?? "없음"}";
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
