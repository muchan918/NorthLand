public enum EnemyType
{
    Melee,
    Ranged,
    Boss,
}

public class EnemyData
{
    public string EnemyID { get; set; }
    public string NameKey { get; set; }
    public EnemyType EnemyType { get; set; }
    public string RoleKey { get; set; }
    public string DescriptionKey { get; set; }
}
