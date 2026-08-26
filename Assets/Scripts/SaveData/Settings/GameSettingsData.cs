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

        public int screenMode = 1;
        public int resolutionIndex = 0;

        public float masterVolume = 1f;
        public float bgmVolume = 0.5f;
        public float sfxVolume = 0.8f;

        public bool masterMuted;
        public bool bgmMuted;
        public bool sfxMuted;

        public static GameSettingsData CreateDefault()
        {
            return new GameSettingsData
            {
                lastSelectedSlotIndex = -1,
                localeCode = "ko-KR",
                keyboardMoveSpeedMultiplier = 1f,
                mouseMoveSpeedMultiplier = 1f,

                screenMode = 1,
                resolutionIndex = 0,

                masterVolume = 1f,
                bgmVolume = 0.5f,
                sfxVolume = 0.8f,

                masterMuted = false,
                bgmMuted = false,
                sfxMuted = false
            };
        }
    }
}