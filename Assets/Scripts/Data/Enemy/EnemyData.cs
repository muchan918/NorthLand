public enum EnemyType
{
    Melee,
    Ranged,
    Boss,
}

public class EnemyData
{
    public string EnemyID { get; set; }
    public string DisplayName { get; set; }
    public EnemyType EnemyType { get; set; }
    public string Role { get; set; }
    public string Description { get; set; }
}
