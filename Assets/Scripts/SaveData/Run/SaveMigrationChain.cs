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

        // #337에서 ResourceKind가 잘리며 사라진 첫 값(Gold). 남는 값은 0~3(Wood/Iron/Food/Mana)이다.
        // enum 상수가 이미 없으므로 리터럴로 둔다 — 이름으로 참조할 대상이 존재하지 않는다.
        private const int FirstRemovedResourceKind = 4;

        public SaveMigrationChain()
       : this(new Dictionary<int, Func<JToken, JToken>>
       {
        { 1, MigrateV1ToV2 },
        { 2, MigrateV2ToV3 }
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

        /// <summary>
        /// v2 → v3 (#337): 삭제된 특수 자원(금·루비·사파이어·다이아 = Kind 4~7) 항목을 걷어낸다.<br/>
        /// <br/>
        /// 이걸 하지 않으면 구 세이브가 <c>RunSaveManager.Management</c>의
        /// <c>Enum.IsDefined</c> 검증에서 "알 수 없는 자원 종류입니다: 4"로 실패하는데,
        /// 그 시점은 이미 월드 생성이 끝난 뒤라 복원 실패 경로가 세이브 파일을 삭제한다.<br/>
        /// <br/>
        /// v2 세이브는 저장 시점의 모든 <c>ResourceKind</c>를 담은 전체 스냅샷이므로,
        /// 4~7만 떨어내면 0~3(나무·철·식량·마나석)이 그대로 남아 "모든 종류 존재" 검증을 통과한다.<br/>
        /// <br/>
        /// 사라진 <c>RunData.Territory</c>·<c>TerritoryRequestedSeed</c> 등은 건드리지 않는다 —
        /// 필드가 없어진 프로퍼티는 <c>SaveSerializer</c>의 <c>MissingMemberHandling.Ignore</c>가 버린다.
        /// </summary>
        private static JToken MigrateV2ToV3(JToken sourceData)
        {
            JToken migratedData = sourceData.DeepClone();

            if (migratedData["Management"]?["Resources"] is not JArray resources)
            {
                return migratedData;
            }

            // 뒤에서부터 지운다 — 앞에서 지우면 남은 항목의 인덱스가 밀린다.
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                JToken kindToken = resources[i]?["Kind"];

                if (kindToken == null || kindToken.Type != JTokenType.Integer)
                {
                    continue; // 형식이 다르면 손대지 않는다 — 검증부가 판단할 몫이다.
                }

                if (kindToken.Value<int>() >= FirstRemovedResourceKind)
                {
                    resources[i].Remove();
                }
            }

            return migratedData;
        }
    }
}
