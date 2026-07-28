public enum EnemyType
{
    Melee,
    Ranged,
    Boss,
}
public enum MovementMode
{
    Ground = 0,
    Flying = 1,
}

public class EnemyData
{
    public string EnemyID { get; set; }
    public string NameKey { get; set; }
    public EnemyType EnemyType { get; set; }
    public string RoleKey { get; set; }
    public string DescriptionKey { get; set; }
}
