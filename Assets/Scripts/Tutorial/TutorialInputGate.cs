using System;
using NorthLand.Combat;

public static class TutorialInputGate
{
    private static bool _restricted;
    private static TutorialAction _allowedActions;
    private static TutorialAction _displayedActions;
    private static int _minimumTowerCountBeforeEndDay;
    private static TowerAsset _requiredTowerBeforeEndDay;
    private static bool _observingTowers;

    public static bool IsRestricted => _restricted;

    public static event Action Changed;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        StopObservingTowers();
        _restricted = false;
        _allowedActions = TutorialAction.None;
        _displayedActions = TutorialAction.None;
        _minimumTowerCountBeforeEndDay = 0;
        _requiredTowerBeforeEndDay = null;
        Changed = null;
    }

    public static bool Allows(TutorialAction action)
        => !_restricted || (_allowedActions & action) == action;

    // 팝업이 물리적으로 입력을 막는 동안에도, 곧 사용할 버튼은 비활성 색으로 만들지 않기 위한 표시 계약.
    public static bool AllowsForDisplay(TutorialAction action)
        => !_restricted || (_displayedActions & action) == action;

    public static bool AllowsEndDay()
        => Allows(TutorialAction.EndDay) && HasRequiredTowers();

    public static void SetEndDayTowerRequirement(int minimumCount, TowerAsset requiredTower)
    {
        int normalizedCount = Math.Max(0, minimumCount);

        if (_minimumTowerCountBeforeEndDay == normalizedCount
            && _requiredTowerBeforeEndDay == requiredTower)
        {
            return;
        }

        _minimumTowerCountBeforeEndDay = normalizedCount;
        _requiredTowerBeforeEndDay = requiredTower;
        RefreshTowerObservation();
        Changed?.Invoke();
    }

    public static void Apply(TutorialAction allowedActions)
    {
        _restricted = true;
        _allowedActions = allowedActions;
        _displayedActions = allowedActions;
        Changed?.Invoke();
    }

    public static void ApplyPopup(TutorialAction displayedActions)
    {
        _restricted = true;
        _allowedActions = TutorialAction.None;
        _displayedActions = displayedActions;
        Changed?.Invoke();
    }

    public static void Clear()
    {
        _restricted = false;
        _allowedActions = TutorialAction.None;
        _displayedActions = TutorialAction.None;
        _minimumTowerCountBeforeEndDay = 0;
        _requiredTowerBeforeEndDay = null;
        StopObservingTowers();
        Changed?.Invoke();
    }

    private static void RefreshTowerObservation()
    {
        if (_minimumTowerCountBeforeEndDay > 0)
        {
            if (_observingTowers)
            {
                return;
            }

            Tower.ActiveChanged += OnTowersChanged;
            _observingTowers = true;
            return;
        }

        StopObservingTowers();
    }

    private static void StopObservingTowers()
    {
        if (!_observingTowers)
        {
            return;
        }

        Tower.ActiveChanged -= OnTowersChanged;
        _observingTowers = false;
    }

    private static void OnTowersChanged() => Changed?.Invoke();

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
