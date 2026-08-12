using System;
using Newtonsoft.Json;

namespace NorthLand.Core
{
    /// <summary>
    /// 저장 파일의 버전과 실제 데이터를 분리하는 공통 봉투.
    /// </summary>
    [Serializable]
    public sealed class SaveEnvelope<TData>
        where TData : class
    {
        [JsonProperty("version", Required = Required.Always)]
        public int Version;

        [JsonProperty("data", Required = Required.Always)]
        public TData Data;

        public SaveEnvelope(int version, TData data)
        {
            Version = version;

            Data = data ??
                throw new ArgumentNullException(nameof(data));
        }
    }
}