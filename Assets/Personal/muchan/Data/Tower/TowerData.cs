public enum TowerType
{
    Single,
    Area,
    Chain,
    Magic,
}

public enum MagicEffectType
{
    None,
    Buff,
    Debuff,
}

public class TowerData
{
    public string TowerID { get; set; }
    public string DisplayName { get; set; }
    public TowerType TowerType { get; set; }
    public MagicEffectType MagicEffectType { get; set; }
    public int GridWidth { get; set; }
    public int GridHeight { get; set; }
    public string Role { get; set; }
    public string Description { get; set; }
}
