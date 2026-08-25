using System;

namespace NorthLand.Core
{
    [Serializable]
    public sealed class GameSettingsData
    {
        public int lastSelectedSlotIndex = -1;
        public string localeCode = "ko-KR";

        public float keyboardMoveSpeedMultiplier = 1f;
        public float mouseMoveSpeedMultiplier = 1f;

        public static GameSettingsData CreateDefault()
        {
            return new GameSettingsData
            {
                lastSelectedSlotIndex = -1,
                localeCode = "ko-KR",
                keyboardMoveSpeedMultiplier = 1f,
                mouseMoveSpeedMultiplier = 1f
            };
        }
    }
}