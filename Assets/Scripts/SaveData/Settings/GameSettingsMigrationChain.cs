using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NorthLand.Core
{
    /// <summary>
    /// settings.json의 버전별 data 마이그레이션 체인을 생성한다.
    /// </summary>
    public static class GameSettingsMigrationChain
    {
        public static SaveMigrationChain Create()
        {
            return new SaveMigrationChain(GameSettingsFormat.OldestSupportedVersion,GameSettingsFormat.CurrentVersion,new Dictionary<int, Func<JToken, JToken>>());
        }
    }
}