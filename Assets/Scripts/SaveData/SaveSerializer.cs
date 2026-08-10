using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace NorthLand.Core
{
    /// <summary>세이브 봉투를 직렬화하고 version을 먼저 검사한 뒤 data를 지연 파싱한다.</summary>
    public sealed class SaveSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include
        };

        private readonly SaveMigrationChain migrationChain;

        public SaveSerializer()
            : this(new SaveMigrationChain())
        {
        }

        internal SaveSerializer(SaveMigrationChain migrationChain)
        {
            this.migrationChain =
                migrationChain ?? throw new ArgumentNullException(nameof(migrationChain));
        }


        public string Serialize(RunData data)
        {
            var envelope = new SaveEnvelope(SaveFormat.CurrentVersion, data);
            return JsonConvert.SerializeObject(envelope, Settings);
        }

        public bool TryDeserialize(string json, out RunData data, out string error)
        {
            data = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "세이브 JSON이 비어 있습니다.";
                return false;
            }

            JObject envelope;
            try
            {
                envelope = JObject.Parse(json);
            }
            catch (JsonException exception)
            {
                error = $"세이브 JSON을 읽을 수 없습니다: {exception.Message}";
                return false;
            }

            JToken versionToken = envelope["version"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
            {
                error = "세이브 봉투에 정수 version이 없습니다.";
                return false;
            }

            int version;
            try
            {
                version = versionToken.Value<int>();
            }
            catch (Exception exception) when (exception is FormatException ||
                                               exception is InvalidCastException ||
                                               exception is OverflowException)
            {
                error = $"세이브 봉투의 version 값이 올바르지 않습니다: {exception.Message}";
                return false;
            }

            JToken rawData = envelope["data"];

            if (!migrationChain.TryMigrate(version, rawData, out JToken currentData, out error))
            {
                return false;
            }

            try
            {
                data = currentData.ToObject<RunData>(JsonSerializer.Create(Settings));
            }
            catch (JsonException exception)
            {
                error = $"세이브 data를 v{SaveFormat.CurrentVersion} 형식으로 읽을 수 없습니다: {exception.Message}";
                return false;
            }

            if (data == null)
            {
                error = "세이브 data가 비어 있습니다.";
                return false;
            }

            return true;
        }
    }
}
