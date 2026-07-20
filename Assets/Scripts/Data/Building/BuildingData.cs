public enum BuildingType
{
    Production,
    General,
    Skill,
}

public class BuildingData
{
    public string BuildingID { get; set; }
    public string NameKey { get; set; }
    public BuildingType BuildingType { get; set; }
    public string RoleKey { get; set; }
    public string DescriptionKey { get; set; }
}
