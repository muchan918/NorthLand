using System;
using Newtonsoft.Json;

namespace NorthLand.Core
{
    /// <summary>
    /// 저장 파일의 버전과 한 판 데이터를 분리하는 봉투.
    /// </summary>
    [Serializable]
    public sealed class SaveEnvelope
    {
        [JsonProperty("version", Required = Required.Always)]
        public int Version;

        [JsonProperty("data", Required = Required.Always)]
        public RunData Data;

        public SaveEnvelope(int version, RunData data)
        {
            Version = version;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }
    }
}
