using System;

namespace NorthLand.Core
{
    /// <summary>
    /// 게임 전체에서 공통으로 사용하는 설정 데이터.
    /// settings.json으로 저장한다.
    /// </summary>
    [Serializable]
    public sealed class GameSettingsData
    {
        public int lastSelectedSlotIndex = -1;

        public string localeCode = "ko-KR";

        public static GameSettingsData CreateDefault()
        {
            return new GameSettingsData
            {
                lastSelectedSlotIndex = -1,
                localeCode = "ko-KR"
            };
        }
    }
}