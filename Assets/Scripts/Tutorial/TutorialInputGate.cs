public static class TutorialInputGate
{
    private static bool _restricted;
    private static TutorialAction _allowedActions;

    public static bool IsRestricted => _restricted;

    public static bool Allows(TutorialAction action)
        => !_restricted || (_allowedActions & action) == action;

    public static void Apply(TutorialAction allowedActions)
    {
        _restricted = true;
        _allowedActions = allowedActions;
    }

    public static void Clear()
    {
        _restricted = false;
        _allowedActions = TutorialAction.None;
    }
}
