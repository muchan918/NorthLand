public static class TowerInfoFormatter
{
    public static string BuildHeader(TowerAsset tower)
    {
        if (tower == null)
            return string.Empty;

        string name = TowerDisplayName.Of(tower);
        TowerData data = TowerDisplayName.EnsureData(tower);

        if (data == null || string.IsNullOrEmpty(data.RoleKey))
            return name;

        string role = LocalizationHelper.Get(LocalizationHelper.k_TowersTable,data.RoleKey);

        return string.IsNullOrEmpty(role)
            ? name
            : $"{name} - {role}";
    }

    public static string BuildDescription(TowerAsset tower)
    {
        if (tower == null)
            return string.Empty;

        TowerData data = TowerDisplayName.EnsureData(tower);

        if (data == null || string.IsNullOrEmpty(data.DescriptionKey))
            return string.Empty;

        return LocalizationHelper.Get(LocalizationHelper.k_TowersTable,data.DescriptionKey);
    }

    public static string BuildStats(TowerAsset tower)
    {
        if (tower == null)
            return string.Empty;

        TowerAsset.AttackFields attack = tower.Attack;

        if (tower.HasAction<NorthLand.Combat.AttackAction>() && attack != null)
        {
            return NorthLand.Combat.TowerStatsFormatter.BuildAttackLines(attack.AttackDamage,attack.AttackRange,attack.AttackInterval);
        }

        return NorthLand.Combat.TowerStatsFormatter.BuildRangeLine(tower.PreviewRadius);
    }
}