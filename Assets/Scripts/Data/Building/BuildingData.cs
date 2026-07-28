// ⚠ 선언 순서가 곧 값이다(명시적 값 없음). BuildingAssetEditor가 enumValueIndex로 캐스팅하고
// SO에는 정수로 직렬화되므로, 새 타입은 반드시 '끝에만' 추가한다(중간 삽입·재정렬 금지).
public enum BuildingType
{
    Production,
    General,
    Skill,
    Store,
}

public class BuildingData
{
    public string BuildingID { get; set; }
    public string NameKey { get; set; }
    public BuildingType BuildingType { get; set; }
    public string RoleKey { get; set; }
    public string DescriptionKey { get; set; }
}
