namespace NorthLand.UI
{
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

            string role = LocalizationHelper.Get(LocalizationHelper.k_TowersTable, data.RoleKey);

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

            return LocalizationHelper.Get(LocalizationHelper.k_TowersTable, data.DescriptionKey);
        }

        public static string BuildStats(TowerAsset tower)
        {
            if (tower == null)
                return string.Empty;

            TowerAsset.AttackFields attack = tower.Attack;

            if (tower.HasAction<NorthLand.Combat.AttackAction>() && attack != null)
            {
                return NorthLand.Combat.TowerStatsFormatter.BuildAttackLines(attack.AttackDamage, attack.AttackRange, attack.AttackInterval);
            }

            // 오라 전용 타워는 「오라 반경」으로 낸다(#536 리뷰) — 정보 패널의 오라 행이 같은 라벨을
            // 쓰므로, 여기만 「사거리」로 두면 배치 전과 배치 후에 같은 값의 이름이 갈린다.
            // ⚠ 빔 타워는 이 분기로 내려오지만(AttackAction이 없다) 오라가 아니라 **사거리**다 —
            // 그래서 "AttackAction이 없다"가 아니라 "오라 액션이 있다"로 판정해야 한다.
            if ((TowerCategoryResolver.Of(tower) & TowerCategory.Aura) != 0)
            {
                return NorthLand.Combat.TowerStatsFormatter.BuildAuraRadiusLine(
                    tower.PreviewRadius
                );
            }

            return NorthLand.Combat.TowerStatsFormatter.BuildRangeLine(
                tower.PreviewRadius
            );
        }
    }
}
