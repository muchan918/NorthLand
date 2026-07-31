// ⚠ 선언 순서가 곧 값이다(명시적 값 없음). BuildingAssetEditor가 enumValueIndex로 캐스팅하고
// SO에는 정수로 직렬화되므로, 새 타입은 반드시 '끝에만' 추가한다(중간 삽입·재정렬 금지).
public enum BuildingType
{
    Production,
    General,
    Skill,
    Store,
    // 본진(#227). General('그 외' 버킷)에서 분리했다 — 주민 증가 테이블이라는 고유 데이터 그룹과
    // 전용 패널을 갖게 되어 Production/Skill/Store와 동급의 1급 분류가 됐다.
    // ⚠ 이 타입은 인스펙터 authoring 분류일 뿐 동작 스위치가 아니다 — 패널 분기는 여전히 데이터 유무로 건다.
    Castle,
}

public class BuildingData
{
    public string BuildingID { get; set; }
    public string NameKey { get; set; }
    public BuildingType BuildingType { get; set; }
    public string RoleKey { get; set; }
    public string DescriptionKey { get; set; }
}
