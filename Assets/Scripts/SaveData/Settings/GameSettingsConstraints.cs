namespace NorthLand.Core
{
    /// <summary>
    /// settings.json에 저장되는 설정값의 공통 범위를 정의한다.
    /// UI와 저장 서비스가 같은 범위를 사용해야 한다.
    /// </summary>
    public static class GameSettingsConstraints
    {
        public const int MinScreenModeIndex = 0;
        public const int MaxScreenModeIndex = 2;

        public const int MinResolutionIndex = 0;
        public const int MaxResolutionIndex = 2;

        public const int ResolutionOptionCount = MaxResolutionIndex - MinResolutionIndex + 1;
    }
}