using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

/// <summary>
/// Localization 동기 조회용 얇은 static 헬퍼. 상태·수명이 없는 순수 유틸이며 "매니저"가 아니다.
/// 전역 서비스(<see cref="LocalizationSettings"/>)를 감싸 호출부 보일러플레이트와 테이블명 문자열 중복만 줄인다.
///
/// 언제 쓰나 / 쓰지 말아야 하나:
/// - ✅ 호버 툴팁처럼 '호출 시점에 한 번' 동기로 값이 필요한 풀(pull) 경로 (BuildingTooltipSource 등).
/// - ❌ 지속형 표시(상세 패널 등)에는 쓰지 말 것 — 여기서 받은 문자열은 로케일이 바뀌어도 자동 갱신되지 않는다.
///      그런 UI는 LocalizeStringEvent / LocalizedString.StringChanged(로케일 변경 자동 구독)를 쓴다.
/// </summary>
public static class LocalizationHelper
{
    // 테이블 컬렉션 이름 상수 — 문자열 하드코딩/오타 방지. 새 테이블이 생기면 여기에 추가한다.
    public const string k_DefaultTable = "NorthLand_default";
    public const string k_BuildingsTable = "NorthLand_buildings";
    public const string k_TowersTable = "NorthLand_Towers";
    public const string k_EnemiesTable = "NorthLand_Enemies";
    public const string k_TerritoriesTable = "NorthLand_Territories";

    /// <summary>
    /// 현재 로케일 기준으로 (테이블, 키)를 동기 해석한다. 내부적으로 로드를 강제 완료하므로
    /// 로컬 테이블에서는 첫 조회에도 키가 그대로 노출되는 프레임이 없다.
    /// (문자열은 TableReference/TableEntryReference로 암시 변환되므로 상수를 그대로 넘기면 된다.)
    /// </summary>
    public static string Get(TableReference table, TableEntryReference entry)
        => LocalizationSettings.StringDatabase.GetLocalizedString(table, entry);

    /// <summary>
    /// 시작 시 테이블을 미리 로드해 첫 조회 히치를 없앤다(선택). 로컬 Addressables 기준 동기 완료.
    /// 동기 <see cref="Get"/>만으로도 키 노출은 없지만, 최초 로드 비용을 부팅 시점으로 옮기고 싶을 때 호출한다.
    /// </summary>
    public static void Warm(params string[] tables)
    {
        foreach (var table in tables)
        {
            LocalizationSettings.StringDatabase.GetTableAsync(table, null).WaitForCompletion();

        }
    }
}
