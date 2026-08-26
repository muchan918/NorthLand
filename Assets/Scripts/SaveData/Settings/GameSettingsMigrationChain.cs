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
            return new SaveMigrationChain(GameSettingsFormat.OldestSupportedVersion,GameSettingsFormat.CurrentVersion,
                new Dictionary<int, Func<JToken, JToken>>
                {
                    [1] = MigrateV1ToV2
                });
        }

        private static JToken MigrateV1ToV2(JToken source)
        {
            JObject data = (JObject)source.DeepClone();

            data["screenMode"] = 1;
            data["resolutionIndex"] = 0;

            data["masterVolume"] = 1f;
            data["bgmVolume"] = 0.5f;
            data["sfxVolume"] = 0.8f;

            data["masterMuted"] = false;
            data["bgmMuted"] = false;
            data["sfxMuted"] = false;

            return data;
        }
    }
}