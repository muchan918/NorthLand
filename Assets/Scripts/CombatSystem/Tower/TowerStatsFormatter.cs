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

        // 라벨 키를 상수로 노출한다(#536). 문자열 줄과 스탯 행(TowerStatRowData)이 **같은 키**를 봐야
        // 한 패널 안에서 같은 스탯의 이름이 갈리지 않는다. 값 서식도 여기 상수를 공유한다.
        public const string k_DamageKey = "game.tower.attack_damage";
        public const string k_RangeKey = "game.tower.attack_range";
        public const string k_SpeedKey = "game.tower.attack_speed";
        public const string k_AuraRadiusKey = "game.tower.aura_radius";
        public const string k_MoveSpeedKey = "game.tower.move_speed";
        public const string k_ArmorKey = "game.tower.armor";
        public const string k_DotKey = "game.tower.dot";
        public const string k_DpsKey = "game.tower.dps";
        public const string k_BurstKey = "game.tower.burst";
        public const string k_ZoneKey = "game.tower.zone";
        public const string k_StackKey = "game.tower.stack";
        public const string k_LockRampKey = "game.tower.lock_ramp";
        public const string k_InheritKey = "game.tower.inherit";
        public const string k_InheritNoneKey = "game.tower.inherit.none";
        public const string k_DefaultFormat = "0.#";
        public const string k_RateFormat = "0.##";

        /// 스탯 행이 라벨을 직접 조회할 수 있게 여는 창구 — 조회 규칙(테이블·헬퍼)은 계속 여기 것이다.
        public static string StatLabel(string key) => Label(key);

        // ── enum → 로컬라이즈 키 ────────────────────────────────────────────
        //
        // ⚠ **매핑에 없는 값은 키를 조회하지 않고 enum 이름을 그대로 돌려준다.**
        // `LocalizationHelper.Get`은 없는 키에 대해 빈 문자열이 아니라 **에러**를 내므로, 새 enum 값을
        // 추가하고 키 등록을 잊으면 콘솔이 매 조회마다 더럽혀진다. 영어 이름이 잠깐 보이는 쪽이 낫다.

        /// 버프 오라가 남에게 거는 축(`ModifiableStat`)은 원장 축(`TowerStat`)과 **다른 열거형**이다 —
        /// 대상이 타워가 아니라 적일 수도 있어(MoveSpeed·Armor) 축 자체가 더 넓다.
        public static string StatName(ModifiableStat stat)
        {
            string key = stat switch
            {
                ModifiableStat.AttackDamage => k_DamageKey,
                ModifiableStat.AttackSpeed => k_SpeedKey,
                ModifiableStat.MoveSpeed => k_MoveSpeedKey,
                ModifiableStat.Armor => k_ArmorKey,
                _ => null,
            };

            return key == null ? stat.ToString() : Label(key);
        }

        /// 공격속도 표시 축: **rate(1÷간격)**. 문자열 줄과 스탯 행이 같은 축을 써야 한다 —
        /// 한쪽만 간격(초)으로 내면 같은 패널에서 커질수록 좋은 수와 작을수록 좋은 수가 섞인다.
        public static float ToRate(float interval) => interval > 0f ? 1f / interval : 0f;

        /// 공격 타워 3줄: 공격력 / 사거리 / 공격속도(=1÷간격).
        ///
        /// ⚠ **정보 패널은 이 메서드를 더 이상 쓰지 않는다**(#536) — 그 세 줄은 스탯 행이 소유한다.
        /// 남은 소비처는 배치 **전** 툴팁(`TowerInfoFormatter.BuildStats`)이다. 그쪽은 타워 인스턴스가
        /// 없어 원장이 없고 행을 만들 수 없으므로 계속 문자열로 낸다.
        public static string BuildAttackLines(float damage, float range, float attackInterval)
        {
            float rate = ToRate(attackInterval);

            return $"{Label(k_DamageKey)}: {damage.ToString(k_DefaultFormat)}\n" +
                   $"{Label(k_RangeKey)}: {range.ToString(k_DefaultFormat)}\n" +
                   $"{Label(k_SpeedKey)}: {rate.ToString(k_RateFormat)}";
        }

        /// 사거리 한 줄. 공격 개념이 있는 타워(빔 포함)가 쓴다.
        public static string BuildRangeLine(float range)
            => $"{Label(k_RangeKey)}: {range.ToString(k_DefaultFormat)}";

        /// 오라 반경 한 줄. **사거리와 라벨을 나눈다**(#536 리뷰).
        ///
        /// <para>예전에는 "플레이어에게는 같은 닿는 거리 개념"이라는 이유로 사거리 라벨을 공유했다.
        /// 표기가 줄 하나였을 때는 성립했지만, 행 기반으로 바뀌면서 <b>한 타워가 두 축을 동시에 낼 수
        /// 있게</b> 됐다 — `Tower`가 설계 목표로 적어 둔 공격+오라 하이브리드가 나오면 「사거리」 라벨이
        /// 붙은 행이 두 개가 되어 어느 쪽이 무엇인지 구분할 수 없다. 현재 하이브리드 프리팹은 0개라
        /// 아직 재현되지 않지만, 라벨을 나누는 비용이 지금이 가장 싸다.</para>
        ///
        /// ⚠ **정보 패널과 배치 전 툴팁이 같은 라벨을 써야 한다** — 그래서 툴팁 쪽
        /// (`TowerInfoFormatter.BuildStats`)도 오라 전용 타워에서는 이 줄을 쓴다. 한쪽만 바꾸면
        /// 배치 전엔 「사거리」, 배치 후엔 「오라 반경」으로 이름이 갈린다.
        public static string BuildAuraRadiusLine(float radius)
            => $"{Label(k_AuraRadiusKey)}: {radius.ToString(k_DefaultFormat)}";

        /// 해금 웨이브 한 줄(#504) — 아직 잠긴 타워의 툴팁에만 붙는다.
        /// **해금 여부는 여기서 판단하지 않는다**(<see cref="TowerAsset.IsUnlocked"/>가 정본) —
        /// 이 클래스는 라벨과 서식만 소유한다. 호출부가 잠긴 타워일 때만 이 줄을 요청한다.
        public static string BuildUnlockWaveLine(int unlockWave)
            => $"{Label("game.tower.unlock_wave")}: {unlockWave}";

        /// 지속 피해 행. 피해가 없으면 null.
        /// **SO 원본이 아니라 실효값(원장 합성 후)을 넘길 것** — 패널 표기와 실제 효과가 어긋나면
        /// WL-079/WL-130이 지적한 "표시부와 적용부가 규칙을 각자 쓰는" 문제가 재발한다.
        /// <param name="label">
        /// 효과 이름(화상·중독). <b>`DoT`로 고정하지 않는다</b> — 합성 후보 툴팁은 「계승: 중독」이라
        /// 부르는데 정보 패널이 「지속 피해」로 부르면 플레이어가 같은 것으로 인식하지 못한다
        /// (<see cref="EffectName"/> 주석의 원칙). 화상과 중독을 함께 가진 타워에서 라벨이 같은 행이
        /// 둘 뜨는 것도 막는다.
        /// </param>
        public static TowerStatRowData? DotRow(string label, float damagePerTick, float tickInterval)
            => damagePerTick > 0f && tickInterval > 0f
                ? TowerStatRowData.Note(label, $"{damagePerTick.ToString(k_DefaultFormat)} / {tickInterval.ToString(k_DefaultFormat)}s")
                : (TowerStatRowData?)null;

        /// 다중 타겟 지속딜(빔) 행 — 대상 1기당 DPS(#298).
        ///
        /// <para><b>`Note`가 아니라 값 쌍이다.</b> 빔의 DPS는 공격력·공격속도 원장을 모두 통과하므로
        /// (<c>DamagePerTick</c>·<c>TickInterval</c>) 버프 타일 위에서 실제로 오른다. `Note`로 내면
        /// 값만 바뀌고 얼마나 올랐는지가 안 보여서, 빔 계열에서만 「기본값 → 적용값」 규칙이 깨진다.</para>
        ///
        /// <para>동시 타격 대상 수는 <b>라벨</b>에 붙인다(`DPS ×3`). 값 칸에 두면 서식 후 문자열 비교가
        /// 접미사까지 포함해 기본값과 실제값이 영원히 달라 보인다.</para>
        public static TowerStatRowData? BeamDpsRow(float baseDps, float dps, int maxTargets)
            => dps > 0f && maxTargets > 0
                ? TowerStatRowData.StatLabeled(
                    maxTargets > 1 ? $"{Label(k_DpsKey)} ×{maxTargets}" : Label(k_DpsKey),
                    baseDps, dps)
                : (TowerStatRowData?)null;

        /// 성장(램프업) 스택 행 — 현재 스택과 상한만 낸다(#300, #536).
        ///
        /// <para><b>배율과 축 이름은 내지 않는다.</b> 성장의 결과는 공격력·공격속도 행이 이미
        /// `기본값 → 실제값`으로 보여주므로 여기서 `×1.2`를 또 내면 같은 사실이 두 번 적힌다.
        /// 축 이름도 마찬가지다 — 어느 스탯이 자랐는지는 화살표가 붙은 행이 답한다.
        /// 배율은 계속 내부 값(원장 modifier)으로만 살아 있고 표시만 걷어낸 것이다.</para>
        ///
        /// <para>상한을 함께 내는 이유는 남는다 — 스택 수만으로는 "이 타워가 어디까지 자라는가"를
        /// 알 수 없어서, 지금 값이 전부인 것으로 오해하게 된다.</para>
        ///
        /// <para>축이 둘인 타워(<c>rampup_tower</c>)도 스택은 공유하므로 행은 하나다. 예전에는 둘째 축의
        /// 배율만 값에 덧붙었는데, 첫째 축은 이름이 없고 둘째만 이름이 붙는 비대칭이 생겨 함께 걷어냈다.</para>
        public static TowerStatRowData? StackRow(int stacks, int maxStacks)
            => maxStacks > 0
                ? TowerStatRowData.Note(Label(k_StackKey), $"{stacks}/{maxStacks}")
                : (TowerStatRowData?)null;

        /// 조준 방식의 표시명(#387). 정책은 키만 알고, 로케일 해석은 여기서 한다.
        ///
        /// ⚠ 이 값은 **조회 시점 스냅샷**이라 로케일이 바뀌어도 자동 갱신되지 않는다
        /// (`LocalizationHelper` 주석의 pull 경로 한계 — 정보 패널 전체가 같은 트레이드오프다, #153).
        /// 패널을 다시 열면 새 로케일로 해석된다.
        public static string TargetingName(TargetingPolicy policy)
            => policy == null ? string.Empty : Label(policy.DisplayNameKey);

        /// 연발 행(#336). 1발이면 null — 대부분의 타워에서 행이 늘지 않는다.
        public static TowerStatRowData? BurstRow(int burstCount)
            => burstCount > 1 ? TowerStatRowData.Note(Label(k_BurstKey), $"×{burstCount}") : (TowerStatRowData?)null;

        /// 착탄 지속 구역 행(#336) — 반경과 남는 시간. 구역이 없으면 null.
        /// 구역이 **거는 효과**는 DoT 행이 따로 낸다(같은 `Effects`를 쓰므로 중복 표기하지 않는다).
        public static TowerStatRowData? GroundZoneRow(float radius, float duration)
            => radius > 0f && duration > 0f
                ? TowerStatRowData.Note(Label(k_ZoneKey), $"{radius.ToString(k_DefaultFormat)} / {duration.ToString(k_DefaultFormat)}s")
                : (TowerStatRowData?)null;

        /// 감속 행. 감속이 없으면(배율 1) null.
        public static TowerStatRowData? SlowRow(float slowMultiplier)
            => slowMultiplier < 1f
                ? TowerStatRowData.Note(EffectName(EffectKind.Slow), $"-{((1f - slowMultiplier) * 100f).ToString(k_DefaultFormat)}%")
                : (TowerStatRowData?)null;

        /// 버프 오라가 부여하는 스탯 변화 행.
        /// 라벨과 값을 가르는 자리가 스탯 이름과 증감량 사이다 — 다른 행들과 같은 열에 정렬된다.
        public static TowerStatRowData? ModifierRow(StatModifier modifier)
        {
            if (modifier == null) return null;

            string sign = modifier.Amount >= 0 ? "+" : "";

            return TowerStatRowData.Note(
                StatName(modifier.Stat),
                $"{sign}{modifier.Amount.ToString(k_DefaultFormat)}{(modifier.IsPercentage ? "%" : "")}");
        }

        /// 합성으로 물려받는 효과 한 줄(#274 Phase 5).
        /// 효과 이름은 DoT/Slow/Stun 줄과 같은 표기를 쓴다 — 툴팁에서 "물려받는다"고 본 이름과
        /// 배치 후 정보 패널에 뜨는 이름이 다르면 플레이어가 같은 것으로 인식하지 못한다.
        ///
        /// ⚠ **null과 빈 집합을 다르게 표시한다**(`ResolveInheritedKinds`와 같은 구분):
        ///   null    계승 개념이 없는 레시피 → 줄 자체를 안 낸다
        ///   빈 집합  계승은 켰는데 물려줄 게 없다 → **"계승: 없음"**을 낸다
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

            return $"{Label(k_InheritKey)}: {joined ?? Label(k_InheritNoneKey)}";
        }

        /// 효과 종류의 표시명. 감속 행(<see cref="SlowRow"/>)·기절 행과 **같은 키를 공유한다** —
        /// 툴팁에서 "물려받는다"고 본 이름과 배치 후 정보 패널에 뜨는 이름이 다르면
        /// 플레이어가 같은 것으로 인식하지 못한다.
        public static string EffectName(EffectKind kind)
        {
            string key = kind switch
            {
                EffectKind.Burn => "game.tower.effect.burn",
                EffectKind.Poison => "game.tower.effect.poison",
                EffectKind.Slow => "game.tower.effect.slow",
                EffectKind.Stun => "game.tower.effect.stun",
                _ => null,
            };

            return key == null ? kind.ToString() : Label(key);
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
