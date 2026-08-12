using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NorthLand.Core
{
    /// <summary>
    /// 공통 { version, data } 봉투를 직렬화하고,
    /// 버전 검사와 마이그레이션 후 실제 데이터로 변환한다.
    /// </summary>
    public sealed class VersionedSaveSerializer<TData> where TData : class
    {
        private static readonly JsonSerializerSettings JsonSettings =
            new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                MissingMemberHandling =MissingMemberHandling.Ignore,
                NullValueHandling =NullValueHandling.Include
            };

        private readonly int currentVersion;

        private readonly SaveMigrationChain migrationChain;

        private readonly string dataName;

        public VersionedSaveSerializer(int currentVersion,SaveMigrationChain migrationChain,string dataName)
        {
            if (currentVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(currentVersion));
            }

            this.currentVersion = currentVersion;

            this.migrationChain =migrationChain ??throw new ArgumentNullException(nameof(migrationChain));

            this.dataName =string.IsNullOrWhiteSpace(dataName)? "저장 데이터": dataName;
        }

        public string Serialize(TData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var envelope = new SaveEnvelope<TData>(currentVersion,data);

            return JsonConvert.SerializeObject(envelope,JsonSettings);
        }

        public bool TryDeserialize(string json,out TData data,out string error)
        {
            data = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = $"{dataName} JSON이 비어 있습니다.";
                return false;
            }

            JObject envelope;

            try
            {
                envelope = JObject.Parse(json);
            }
            catch (JsonException exception)
            {
                error =$"{dataName} JSON을 읽을 수 없습니다: " +exception.Message;

                return false;
            }

            JToken versionToken = envelope["version"];

            if (versionToken == null ||versionToken.Type != JTokenType.Integer)
            {
                error = $"{dataName} 봉투에 정수 version이 없습니다.";

                return false;
            }

            int version;

            try
            {
                version = versionToken.Value<int>();
            }
            catch (Exception exception) when (exception is FormatException ||exception is InvalidCastException ||exception is OverflowException)
            {
                error = $"{dataName} 봉투의 version 값이 올바르지 않습니다: " + exception.Message;

                return false;
            }

            JToken rawData = envelope["data"];

            if (!migrationChain.TryMigrate(version,rawData,out JToken currentData,out error))
            {
                return false;
            }

            try
            {
                data = currentData.ToObject<TData>(JsonSerializer.Create(JsonSettings));
            }
            catch (JsonException exception)
            {
                error =$"{dataName} data를 읽을 수 없습니다: " +exception.Message;

                return false;
            }

            if (data == null)
            {
                error = $"{dataName} data가 비어 있습니다.";
                return false;
            }

            return true;
        }
    }
}