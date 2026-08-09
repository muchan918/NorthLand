using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NorthLand.Core
{
    /// <summary>
    /// 세이브의 data 토큰을 v1→v2처럼 한 버전씩 순차 변환한다.
    /// 등록된 인접 버전 마이그레이션을 순서대로 적용한다.
    /// </summary>
    public sealed class SaveMigrationChain
    {
        private readonly IReadOnlyDictionary<int, Func<JToken, JToken>> migrations;

        public SaveMigrationChain()
       : this(new Dictionary<int, Func<JToken, JToken>>
       {
        { 1, MigrateV1ToV2 }
       })
        {
        }

        internal SaveMigrationChain(IReadOnlyDictionary<int, Func<JToken, JToken>> migrations)
        {
            this.migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
        }

        public bool TryMigrate(int sourceVersion, JToken sourceData, out JToken migratedData, out string error)
        {
            migratedData = sourceData;
            error = null;

            if (sourceVersion < SaveFormat.OldestSupportedVersion)
            {
                error = $"지원하지 않는 과거 세이브 버전입니다. 저장 버전: {sourceVersion}, 최소 지원 버전: {SaveFormat.OldestSupportedVersion}";
                return false;
            }

            if (sourceVersion > SaveFormat.CurrentVersion)
            {
                error = $"현재 빌드보다 새로운 세이브 버전입니다. 저장 버전: {sourceVersion}, 현재 버전: {SaveFormat.CurrentVersion}";
                return false;
            }

            if (migratedData == null || migratedData.Type == JTokenType.Null)
            {
                error = "세이브 봉투의 data가 비어 있습니다.";
                return false;
            }

            for (int version = sourceVersion; version < SaveFormat.CurrentVersion; version++)
            {
                if (!migrations.TryGetValue(version, out Func<JToken, JToken> migrate))
                {
                    error = $"세이브 마이그레이션 경로가 없습니다. v{version} → v{version + 1}";
                    return false;
                }

                try
                {
                    migratedData = migrate(migratedData);
                }
                catch (Exception exception)
                {
                    error = $"세이브 마이그레이션에 실패했습니다. v{version} → v{version + 1}: {exception.Message}";
                    return false;
                }

                if (migratedData == null || migratedData.Type == JTokenType.Null)
                {
                    error = $"세이브 마이그레이션 결과가 비어 있습니다. v{version} → v{version + 1}";
                    return false;
                }
            }


            return true;
        }

        private static JToken MigrateV1ToV2(JToken sourceData)
        {
            JToken migratedData = sourceData.DeepClone();

            if (migratedData["Towers"] is not JArray towers)
            {
                return migratedData;
            }

            foreach (JToken tower in towers)
            {
                if (tower is not JObject towerObject)
                {
                    continue;
                }

                // v1의 모든 타워는 기존 자동 생성 배틀맵에 배치되어 있다.
                towerObject["MapArea"] = (int)MapArea.CombatMap;
                towerObject["StartTileId"] = null;
            }

            return migratedData;
        }
    }
}
