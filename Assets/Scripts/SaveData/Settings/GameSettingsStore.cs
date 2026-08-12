using System;
using Newtonsoft.Json;

namespace NorthLand.Core
{
    /// <summary>
    /// 게임 공통 settings.json을 저장하고 불러온다.
    /// </summary>
    public sealed class GameSettingsStore
    {
        private const string SettingsFileName = "settings.json";

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Include
            };

        private readonly SaveFileStore fileStore;

        public bool Exists => fileStore.Exists;

        public string SavePath => fileStore.SavePath;

        public GameSettingsStore(string saveRootPath)
        {
            if (string.IsNullOrWhiteSpace(saveRootPath))
            {
                throw new ArgumentException("설정 저장 경로가 비어 있습니다.",nameof(saveRootPath));
            }

            fileStore = new SaveFileStore(saveRootPath,SettingsFileName);
        }

        public bool TrySave(GameSettingsData data,out string error)
        {
            error = null;

            if (data == null)
            {
                error = "저장할 설정 데이터가 없습니다.";
                return false;
            }

            string json;

            try
            {
                json = JsonConvert.SerializeObject(data,JsonSettings);
            }
            catch (JsonException exception)
            {
                error = $"설정 데이터 직렬화에 실패했습니다: {exception.Message}";

                return false;
            }

            return fileStore.TryWrite(json, out error);
        }

        public bool TryLoad(out GameSettingsData data,out string error)
        {
            data = null;
            error = null;

            if (!fileStore.TryRead(out string json, out error))
            {
                return false;
            }

            try
            {
                data = JsonConvert.DeserializeObject<GameSettingsData>(json,JsonSettings);
            }
            catch (JsonException exception)
            {
                error = $"설정 데이터를 읽을 수 없습니다: {exception.Message}";

                return false;
            }

            if (data == null)
            {
                error = "설정 데이터가 비어 있습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(data.localeCode))
            {
                data.localeCode = "ko-KR";
            }

            return true;
        }

        public bool TryDelete(out string error)
        {
            return fileStore.TryDelete(out error);
        }
    }
}