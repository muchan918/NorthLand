/// <summary>
/// 타워 표시 이름의 단일 출처. <c>TowerID</c> → <see cref="TowerData.NameKey"/> → <c>NorthLand_Towers</c>
/// 로컬라이즈로 풀어내고, 어느 단계가 비면 그 단계의 폴백으로 내려간다.
/// <br/>
/// <b>왜 모아 두는가</b>: 같은 해석 규칙이 합성 패널·도감·정보 패널·툴팁 4곳에서 필요한데, 각자 구현하면
/// 폴백이 어긋난다. 실제로 도감은 <c>TowerID</c>가 비었을 때 SO 파일명으로 내려갔지만 합성 패널은
/// <c>"?"</c>만 냈고, <see cref="Data"/> 채움을 어느 쪽이 책임지는지도 호출부마다 달랐다.
/// 이름은 플레이어가 타워를 식별하는 유일한 수단이라 폴백이 갈리면 같은 타워가 화면마다 다르게 불린다.
/// </summary>
public static class TowerDisplayName
{
    /// <summary>
    /// <see cref="TowerAsset.Data"/>(런타임 전용 — 에셋에 직렬화되지 않는다)를 채워 반환한다.
    /// 이름·역할·설명을 읽는 모든 경로가 이걸 먼저 통과해야 배치 경로를 거치지 않은 인스턴스
    /// (합성 후보 버튼의 결과 타워, 도감 항목, 테스트 씬 등)에서도 키 조회가 성립한다.
    /// </summary>
    // `?.`가 필수다 — DataTableManager.Get&lt;T&gt;는 테이블 미등록 시 LogError 후 **null을 반환**한다.
    // 이게 없으면 TowerTable이 등록되지 않은 씬에서 NRE가 난다(Tower.OnSelected의 같은 주석 참조).
    public static TowerData EnsureData(TowerAsset asset)
    {
        if (asset == null) return null;

        return asset.Data ??= DataTableManager.Get<TowerTable>("TowerTable")?.Get(asset.TowerID);
    }

    /// <summary>
    /// 표시 이름을 반환한다. 폴백 순서: 로컬라이즈된 이름 → <c>TowerID</c> → SO 파일명 → <c>"?"</c>.
    /// 어느 단계에서도 예외를 던지지 않는다 — 저작이 덜 된 타워를 클릭했을 때 패널이 죽는 것보다
    /// 식별자라도 뜨는 게 낫다.
    /// </summary>
    public static string Of(TowerAsset asset)
    {
        if (asset == null) return "?";

        // TowerID가 비면 테이블 조회 자체가 불가능하다 — SO 파일명이 그나마 저작자가 알아볼 이름이다.
        if (string.IsNullOrWhiteSpace(asset.TowerID)) return asset.name;

        TowerData data = EnsureData(asset);
        if (data == null || string.IsNullOrWhiteSpace(data.NameKey)) return asset.TowerID;

        string localized = LocalizationHelper.Get(LocalizationHelper.k_TowersTable, data.NameKey);
        return string.IsNullOrWhiteSpace(localized) ? asset.TowerID : localized;
    }
}
