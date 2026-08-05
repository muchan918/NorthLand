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
    const string k_CastCountKey = "skills.stat.cast_count";
    
    // 상한 도달 표기. 언어와 무관하게 통용되는 토큰이라 로컬라이제이션 테이블에 넣지 않는다(#292).
    const string k_MaxText = "Max";

    static string Label(string key) => LocalizationHelper.Get(LocalizationHelper.k_SkillsTable, key);

    // 보상 카드는 "고르면 어떻게 바뀌는지"를 보여주므로 모든 줄이 현재값 → 획득 후 값 형태다.
    // 화살표는 BuildingInfoUI의 업그레이드 표기(주민당 5 → 7)와 같은 문자를 쓴다.
    const string k_Arrow = " → ";

    /// 보상 카드의 레벨 줄. 미보유는 "Lv 0 → Lv 1", 이번 선택으로 상한에 닿으면 "Lv 2 → Max"(#292).
    public static string BuildLevelLine(int current, int next, bool nextIsMax)
        => $"{Label(k_LevelKey)} {current}{k_Arrow}{(nextIsMax ? k_MaxText : $"{Label(k_LevelKey)} {next}")}";

    /// 화상·버프 화상 공용 틱 데미지 한 줄.
    public static string BuildTickDamageLine(float current, float next)
        => $"{Label(k_TickDamageKey)}: {current:0.#}{k_Arrow}{next:0.#}";

    /// 폭탄 2줄: 폭발 데미지 / 반경. 반경은 레벨과 무관한 고정값이라 화살표 없이 한 값만 보여준다.
    public static string BuildBombLines(float currentDamage, float nextDamage, float radius)
        => $"{Label(k_BombDamageKey)}: {currentDamage:0.#}{k_Arrow}{nextDamage:0.#}\n" +
           $"{Label(k_RadiusKey)}: {radius:0.#}";

    /// 추가시전 총 발동 횟수 한 줄.
    public static string BuildCastCountLine(int current, int next)
        => $"{Label(k_CastCountKey)}: {current}{k_Arrow}{next}";
}
