using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NorthLand.Core
{
    /// <summary>
    /// player.json의 버전별 data 마이그레이션 체인을 생성한다.
    /// </summary>
    public static class PlayerSaveMigrationChain
    {
        public static SaveMigrationChain Create()
        {
            return new SaveMigrationChain(
                PlayerSaveFormat.OldestSupportedVersion,
                PlayerSaveFormat.CurrentVersion,
                new Dictionary<int, Func<JToken, JToken>>
                {
                    { 1, MigrateV1ToV2 }
                });
        }

        private static JToken MigrateV1ToV2(JToken sourceData)
        {
            JToken migratedData = sourceData.DeepClone();

            if (migratedData is JObject playerData)
            {
                playerData["tutorialCompleted"] = false;
            }

            return migratedData;
        }
    }
}
