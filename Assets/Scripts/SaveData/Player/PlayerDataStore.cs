using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NorthLand.Core
{
    /// <summary>
    /// 한 플레이어 슬롯의 player.json을 저장하고 불러온다.
    /// </summary>
    public sealed class PlayerDataStore
    {
        private const string PlayerFileName = "player.json";

        private readonly VersionedSaveSerializer<PlayerData> serializer =new VersionedSaveSerializer<PlayerData>(PlayerSaveFormat.CurrentVersion,PlayerSaveMigrationChain.Create(),"플레이어 데이터");

        private readonly SaveFileStore fileStore;

        private readonly SaveMigrationChain legacyMigrationChain = PlayerSaveMigrationChain.Create();

        public bool Exists => fileStore.Exists;

        public string SavePath => fileStore.SavePath;

        public PlayerDataStore(string slotPath)
        {
            if (string.IsNullOrWhiteSpace(slotPath))
            {
                throw new ArgumentException("플레이어 슬롯 경로가 비어 있습니다.",nameof(slotPath));
            }

            fileStore = new SaveFileStore(slotPath,PlayerFileName);
        }

        public bool TrySave(PlayerData data,out string error)
        {
            error = null;

            if (data == null)
            {
                error = "저장할 플레이어 데이터가 없습니다.";
                return false;
            }

            string json;

            try
            {
                json = serializer.Serialize(data);
            }
            catch (JsonException exception)
            {
                error = $"플레이어 데이터 직렬화에 실패했습니다: " +exception.Message;

                return false;
            }
            catch (ArgumentException exception)
            {
                error = $"플레이어 데이터 직렬화에 실패했습니다: " +exception.Message;

                return false;
            }

            return fileStore.TryWrite(json, out error);
        }

        public bool TryLoad(out PlayerData data,out string error)
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
                error =$"플레이어 데이터를 읽을 수 없습니다: " +exception.Message;

                return false;
            }

            bool isEnvelope = root["data"] != null;
            bool needsRewrite = false;

            if (isEnvelope)
            {
                // 새 { version, data } 봉투 형식
                if (!serializer.TryDeserialize(json,out data,out error))
                {
                    return false;
                }
            }
            else
            {
                // 기존 평면 player.json 형식
                JToken versionToken = root["version"];

                if (versionToken == null ||versionToken.Type != JTokenType.Integer)
                {
                    error ="구버전 플레이어 데이터에 정수 version이 없습니다.";

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
                    error = $"구버전 플레이어 데이터의 version 값이 올바르지 않습니다: {exception.Message}";

                    return false;
                }

                if (!legacyMigrationChain.TryMigrate(version,root,out JToken migratedData,out error))
                {
                    return false;
                }

                try
                {
                    data = migratedData.ToObject<PlayerData>();
                }
                catch (JsonException exception)
                {
                    error =$"구버전 플레이어 데이터를 읽을 수 없습니다: " +exception.Message;

                    return false;
                }

                needsRewrite = true;
            }

            // 새 형식과 구버전 형식에 공통 검증
            if (data == null)
            {
                error = "플레이어 데이터가 비어 있습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(data.playerId))
            {
                error = "플레이어 ID가 비어 있습니다.";
                data = null;
                return false;
            }

            // 정상적인 구버전 평면 JSON을 새 봉투 형식으로 다시 저장
            if (needsRewrite &&
                !TrySave(data, out error))
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