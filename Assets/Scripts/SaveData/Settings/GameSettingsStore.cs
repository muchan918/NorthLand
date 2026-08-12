using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NorthLand.Core
{
    /// <summary>
    /// 게임 공통 settings.json을 저장하고 불러온다.
    /// </summary>
    public sealed class GameSettingsStore
    {
        private const string SettingsFileName = "settings.json";

        private readonly VersionedSaveSerializer<GameSettingsData> serializer = new VersionedSaveSerializer<GameSettingsData>(GameSettingsFormat.CurrentVersion,GameSettingsMigrationChain.Create(),"게임 설정");

        private readonly SaveFileStore fileStore;

        public bool Exists => fileStore.Exists;

        public string SavePath => fileStore.SavePath;

        private readonly SaveMigrationChain legacyMigrationChain = GameSettingsMigrationChain.Create();

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
                json = serializer.Serialize(data);
            }
            catch (JsonException exception)
            {
                error = $"설정 데이터 직렬화에 실패했습니다: " +exception.Message;

                return false;
            }
            catch (ArgumentException exception)
            {
                error = $"설정 데이터 직렬화에 실패했습니다: " +exception.Message;

                return false;
            }

            return fileStore.TryWrite(json, out error);
        }

        public bool TryLoad(out GameSettingsData data,out string error)
        {
            data = null;
            error = null;

            if (!fileStore.TryRead(out string json,out error))
            {
                return false;
            }

            JObject root;

            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException exception)
            {
                error = $"설정 데이터를 읽을 수 없습니다: " +exception.Message;

                return false;
            }

            bool isEnvelope = root["data"] != null;
            bool needsRewrite = false;

            if (isEnvelope)
            {
                if (!serializer.TryDeserialize(json,out data,out error))
                {
                    return false;
                }
            }
            else
            {
                JToken versionToken = root["version"];

                if (versionToken == null || versionToken.Type != JTokenType.Integer)
                {
                    error = "구버전 설정 데이터에 정수 version이 없습니다.";

                    return false;
                }

                int version;

                try
                {
                    version = versionToken.Value<int>();
                }
                catch (Exception exception)
                    when (exception is FormatException ||
                          exception is InvalidCastException ||
                          exception is OverflowException)
                {
                    error = $"구버전 설정 데이터의 version 값이 올바르지 않습니다: {exception.Message}";

                    return false;
                }

                if (!legacyMigrationChain.TryMigrate(version,root,out JToken migratedData,out error))
                {
                    return false;
                }

                try
                {
                    data = migratedData.ToObject<GameSettingsData>();
                }
                catch (JsonException exception)
                {
                    error = $"구버전 설정 데이터를 읽을 수 없습니다: " +exception.Message;

                    return false;
                }

                needsRewrite = true;
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

            if (data.lastSelectedSlotIndex < -1 ||data.lastSelectedSlotIndex >=PlayerSlotManager.SlotCount)
            {
                data.lastSelectedSlotIndex = -1;
            }

            if (needsRewrite &&!TrySave(data, out error))
            {
                data = null;
                return false;
            }

            return true;
        }

        public bool TryDelete(out string error)
        {
            return fileStore.TryDelete(out error);
        }
    }
}