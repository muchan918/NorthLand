using System;
using Newtonsoft.Json;

namespace NorthLand.Core
{
    /// <summary>
    /// 한 플레이어 슬롯의 player.json을 저장하고 불러온다.
    /// </summary>
    public sealed class PlayerDataStore
    {
        private const string PlayerFileName = "player.json";

        private static readonly JsonSerializerSettings JsonSettings =
            new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Include
            };

        private readonly SaveFileStore fileStore;

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
                json = JsonConvert.SerializeObject(data,JsonSettings);
            }
            catch (JsonException exception)
            {
                error = $"플레이어 데이터 직렬화에 실패했습니다: {exception.Message}";

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

            try
            {
                data = JsonConvert.DeserializeObject<PlayerData>(json,JsonSettings);
            }
            catch (JsonException exception)
            {
                error = $"플레이어 데이터를 읽을 수 없습니다: {exception.Message}";

                return false;
            }

            if (string.IsNullOrWhiteSpace(data.playerId))
            {
                error = "플레이어 ID가 비어 있습니다.";
                data = null;
                return false;
            }

            if (string.IsNullOrWhiteSpace(data.playerName))
            {
                error = "플레이어 이름이 비어 있습니다.";
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