public enum BuildingType
{
    Production,
    General,
    Skill,
}

public class BuildingData
{
    public string BuildingID { get; set; }
    public string DisplayName { get; set; }
    public BuildingType BuildingType { get; set; }
    public string Role { get; set; }
    public string Description { get; set; }
}
