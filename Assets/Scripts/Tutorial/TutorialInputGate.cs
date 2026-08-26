using System;
using NorthLand.Combat;

public static class TutorialInputGate
{
    private static bool _restricted;
    private static TutorialAction _allowedActions;
    private static int _minimumTowerCountBeforeEndDay;
    private static TowerAsset _requiredTowerBeforeEndDay;

    public static bool IsRestricted => _restricted;

    public static event Action Changed;

    public static bool Allows(TutorialAction action)
        => !_restricted || (_allowedActions & action) == action;

    public static bool AllowsEndDay()
        => Allows(TutorialAction.EndDay) && HasRequiredTowers();

    public static void SetEndDayTowerRequirement(int minimumCount, TowerAsset requiredTower)
    {
        _minimumTowerCountBeforeEndDay = Math.Max(0, minimumCount);
        _requiredTowerBeforeEndDay = requiredTower;
    }

    public static void Apply(TutorialAction allowedActions)
    {
        _restricted = true;
        _allowedActions = allowedActions;
        Changed?.Invoke();
    }

    public static void Clear()
    {
        _restricted = false;
        _allowedActions = TutorialAction.None;
        SetEndDayTowerRequirement(0, null);
        Changed?.Invoke();
    }

    private static bool HasRequiredTowers()
    {
        if (_minimumTowerCountBeforeEndDay <= 0)
        {
            return true;
        }

        int count = 0;

        for (int i = 0; i < Tower.Active.Count; i++)
        {
            Tower tower = Tower.Active[i];

            if (tower != null
                && (_requiredTowerBeforeEndDay == null || tower.Asset == _requiredTowerBeforeEndDay))
            {
                count++;
            }
        }

        return count >= _minimumTowerCountBeforeEndDay;
    }
}
