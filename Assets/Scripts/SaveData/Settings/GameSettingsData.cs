using System;

[Serializable]
public sealed class GameSettingsData
{
    public int lastSelectedSlotIndex = -1;
    public string localeCode = "ko-KR";

    // 0.5 ~ 2.0 사이의 속도 배율
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