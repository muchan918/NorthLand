public enum ResourceKind
{
    Wood,
    Iron,
    Food,
    Mana,
}

public class ResourceData
{
    public string ResourceID { get; set; }
    public string DisplayName { get; set; }
    public ResourceKind Kind { get; set; }
}
