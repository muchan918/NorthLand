// 스킬 특수효과(#169) 수치 표시 문자열의 단일 출처(#287).
//
// TowerStatsFormatter와 같은 역할이며 서식도 맞춘다 — 보상 패널과 타워 정보 패널이 같은 종류의
// 정보(스탯 수치)를 보여주므로 표기가 갈리면 안 된다.
// 라벨 조회와 숫자 서식이 여기 한 곳에만 있으므로, 표기를 바꿀 때 효과 클래스들을 찾아다닐
// 필요가 없다(WL-079가 지적한 "같은 라벨 조회가 여러 곳에 복제" 문제 예방).
public static class SkillStatsFormatter
{
    // 스킬 스탯 라벨은 스킬 전용 테이블에 둔다 — 타워 스탯 라벨(NorthLand_default의 game.tower.*)과
    // 갈리지만, 스킬 문자열이 계속 늘어날 예정이고 default는 전원이 편집하는 단일 에셋이라
    // 병합 충돌을 피하려고 소유권 분리를 택했다.
    const string k_LevelKey      = "skills.stat.level";
    const string k_TickDamageKey = "skills.stat.tick_damage";
    const string k_BombDamageKey = "skills.stat.bomb_damage";
    const string k_RadiusKey     = "skills.stat.bomb_radius";
    const string k_ChargeCountKey = "skills.stat.charge_count";
    const string k_FieldTickDamageKey = "skills.stat.field_tick_damage";
    const string k_FieldRadiusKey     = "skills.stat.field_radius";
    const string k_ExecuteThresholdKey = "skills.stat.execute_threshold";

    // 마법 연구소 강화 미리보기용(#398). 위 키들이 보상 특수효과의 스탯이라면 이 셋은 감전 본체의
    // 스탯이다 — 같은 종류의 정보라 같은 테이블·같은 서식을 쓴다.
    const string k_SkillDamageKey   = "skills.stat.damage";
    const string k_SkillRadiusKey   = "skills.stat.radius";
    const string k_SkillCooldownKey = "skills.stat.cooldown";

    // 재충전만 단위가 붙는다("10초 → 9초"). 단위를 라벨 문자열에 넣으면 언어별 표기(초/s/秒)를
    // 코드가 하드코딩하게 되고, 줄 전체를 Smart String("재충전 시간 {0}초 → {1}초")으로 만들면
    // 화살표가 로컬라이즈 문자열 안으로 들어가 k_Arrow 단일 출처가 깨진다. 단위만 키로 빼면 둘 다 지킨다.
    const string k_SecondUnitKey = "skills.stat.unit.second";
    
    // 상한 도달 표기. 언어와 무관하게 통용되는 토큰이라 로컬라이제이션 테이블에 넣지 않는다(#292).
    const string k_MaxText = "Max";

    static string Label(string key) => LocalizationHelper.Get(LocalizationHelper.k_SkillsTable, key);

    // 보상 카드는 "고르면 어떻게 바뀌는지"를 보여주므로 모든 줄이 현재값 → 획득 후 값 형태다.
    // 화살표는 BuildingInfoUI의 업그레이드 표기(주민당 5 → 7)와 같은 문자를 쓴다.
    const string k_Arrow = " → ";

    /// 보상 카드의 레벨 줄. 미보유는 "Lv 0 → Lv 1", 이번 선택으로 상한에 닿으면 "Lv 2 → Max"(#292).
    public static string BuildLevelLine(int current, int next, bool nextIsMax)
        => $"{Label(k_LevelKey)} {current}{k_Arrow}{(nextIsMax ? k_MaxText : $"{Label(k_LevelKey)} {next}")}";

    /// 획득 목록 툴팁의 레벨 줄. 보상 카드와 달리 미리보기가 아니라 지금 보유한 레벨만 보여준다("Lv 2").
    public static string BuildCurrentLevelLine(int level)
        => $"{Label(k_LevelKey)} {level}";

    /// 화상·버프 화상 공용 틱 데미지 한 줄.
    public static string BuildTickDamageLine(float current, float next)
        => $"{Label(k_TickDamageKey)}: {current:0.#}{k_Arrow}{next:0.#}";

    /// 폭탄 2줄: 폭발 데미지 / 반경. 반경은 레벨과 무관한 고정값이라 화살표 없이 한 값만 보여준다.
    public static string BuildBombLines(float currentDamage, float nextDamage, float radius)
        => $"{Label(k_BombDamageKey)}: {currentDamage:0.#}{k_Arrow}{nextDamage:0.#}\n" +
           $"{Label(k_RadiusKey)}: {radius:0.#}";

    /// 추가시전 충전 횟수 한 줄.
    public static string BuildChargeCountLine(int current, int next)
        => $"{Label(k_ChargeCountKey)}: {current}{k_Arrow}{next}";

    /// 전기장 3줄: 틱 데미지 / 반경 / 감속. 반경은 고정값이고 감속은 레벨에 따라 강해진다.
    public static string BuildFieldLines(
        float currentDamage,
        float nextDamage,
        float radius,
        float currentSlowMultiplier,
        float nextSlowMultiplier)
        => $"{Label(k_FieldTickDamageKey)}: {currentDamage:0.#}{k_Arrow}{nextDamage:0.#}\n" +
           $"{Label(k_FieldRadiusKey)}: {radius:0.#}\n" +
           $"{NorthLand.Combat.TowerStatsFormatter.EffectName(NorthLand.Combat.EffectKind.Slow)}: " +
           $"-{(1f - currentSlowMultiplier) * 100f:0.#}%{k_Arrow}" +
           $"-{(1f - nextSlowMultiplier) * 100f:0.#}%";

    /// 처형 임계 한 줄. 값이 MaxHp 대비 비율(0~1)이라 백분율로 표기한다("10% → 20%").
    /// P0 서식을 쓰지 않는 이유: ko-KR PercentPositivePattern이 "10 %"처럼 공백을 넣고,
    /// 그 거동이 CultureInfo.CurrentCulture(= OS 로케일) 의존이라 표기가 기기마다 갈린다.
    /// 여기서 직접 ×100 하므로 호출부는 비율(0.1f)을 그대로 넘긴다.
    public static string BuildExecuteThresholdLine(float current, float next)
        => $"{Label(k_ExecuteThresholdKey)}: {current * 100f:0.#}%{k_Arrow}{next * 100f:0.#}%";

    // ── 마법 연구소 강화 미리보기(#398) ──────────────────────────────
    // 건물 정보 패널이 "이번 업그레이드로 감전이 어떻게 변하는가"를 보여줄 때 쓴다. 보상 카드와 같은
    // 서식을 쓰는 이유는 위 메서드들과 같다 — 두 화면이 같은 종류의 정보(스탯 수치)를 보여주므로
    // 표기가 갈리면 안 된다.

    /// 감전 데미지 한 줄. "데미지: 30 → 36"
    public static string BuildSkillDamageLine(float current, float next)
        => $"{Label(k_SkillDamageKey)}: {current:0.#}{k_Arrow}{next:0.#}";

    /// 감전 범위 한 줄. "공격 범위: 6 → 9"
    public static string BuildSkillRadiusLine(float current, float next)
        => $"{Label(k_SkillRadiusKey)}: {current:0.#}{k_Arrow}{next:0.#}";

    /// 재충전 간격 한 줄. "재충전 시간: 10초 → 9초" — 이 스탯만 낮을수록 이득이라 값이 줄어든다.
    public static string BuildSkillCooldownLine(float current, float next)
    {
        string unit = Label(k_SecondUnitKey);
        return $"{Label(k_SkillCooldownKey)}: {current:0.#}{unit}{k_Arrow}{next:0.#}{unit}";
    }
}
