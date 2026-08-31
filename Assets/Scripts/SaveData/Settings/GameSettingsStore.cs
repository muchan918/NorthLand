using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;

namespace NorthLand.Core
{
    /// <summary>
    /// 게임 공통 settings.json을 저장하고 불러온다.
    /// </summary>
    public sealed class GameSettingsStore
    {
        public enum GameSettingsLoadFailure
        {
            None,
            Corrupted,
            UnsupportedVersion,
            IoFailure
        }

        private const string SettingsFileName = "settings.json";
        private const int MaxCorruptedBackupCount = 3;

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

        public bool TryLoad(out GameSettingsData data,out GameSettingsLoadFailure failure,out string error)
        {
            data = null;
            failure = GameSettingsLoadFailure.None;
            error = null;

            if (!fileStore.TryRead(out string json, out error))
            {
                failure = GameSettingsLoadFailure.IoFailure;
                return false;
            }

            JObject root;

            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException exception)
            {
                failure = GameSettingsLoadFailure.Corrupted;
                error = $"설정 데이터를 읽을 수 없습니다: {exception.Message}";
                return false;
            }

            // Envelope 형식과 구버전 평면 형식 모두 최상위 version을 사용한다.
            JToken versionToken = root["version"];

            if (versionToken == null || versionToken.Type != JTokenType.Integer)
            {
                failure = GameSettingsLoadFailure.Corrupted;
                error = "설정 데이터에 정수 version이 없습니다.";
                return false;
            }

            int version;

            try
            {
                version = versionToken.Value<int>();
            }
            catch (Exception exception)
                when (exception is FormatException ||exception is InvalidCastException ||exception is OverflowException)
            {
                failure = GameSettingsLoadFailure.Corrupted;
                error = $"설정 데이터의 version 값이 올바르지 않습니다: {exception.Message}";
                return false;
            }

            // 상위 버전뿐 아니라 더 이상 지원하지 않는 과거 버전도
            // 손상 파일로 취급하거나 격리하지 않는다.
            if (version < GameSettingsFormat.OldestSupportedVersion)
            {
                failure = GameSettingsLoadFailure.UnsupportedVersion;
                error =$"지원하지 않는 과거 설정 버전입니다. " +$"저장 버전: {version}, " +$"최소 지원 버전: {GameSettingsFormat.OldestSupportedVersion}";

                return false;
            }

            if (version > GameSettingsFormat.CurrentVersion)
            {
                failure = GameSettingsLoadFailure.UnsupportedVersion;
                error =$"현재 빌드보다 새로운 설정 버전입니다. " +$"저장 버전: {version}, " +$"현재 버전: {GameSettingsFormat.CurrentVersion}";

                return false;
            }

            bool isEnvelope = root["data"] != null;
            bool needsRewrite = false;

            if (isEnvelope)
            {
                if (!serializer.TryDeserialize(json, out data, out error))
                {
                    // 버전 범위는 위에서 확인했으므로 여기서의 실패는
                    // data 누락, 마이그레이션 불가, 역직렬화 실패 등이다.
                    failure = GameSettingsLoadFailure.Corrupted;
                    return false;
                }
            }
            else
            {
                if (!legacyMigrationChain.TryMigrate(
                        version,
                        root,
                        out JToken migratedData,
                        out error))
                {
                    failure = GameSettingsLoadFailure.Corrupted;
                    return false;
                }

                try
                {
                    data = migratedData.ToObject<GameSettingsData>();
                }
                catch (JsonException exception)
                {
                    failure = GameSettingsLoadFailure.Corrupted;
                    error =
                        $"구버전 설정 데이터를 읽을 수 없습니다: " +
                        exception.Message;

                    return false;
                }

                needsRewrite = true;
            }

            if (data == null)
            {
                failure = GameSettingsLoadFailure.Corrupted;
                error = "설정 데이터가 비어 있습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(data.localeCode))
            {
                data.localeCode = "ko-KR";
            }

            if (data.lastSelectedSlotIndex < -1 ||
                data.lastSelectedSlotIndex >= PlayerSlotManager.SlotCount)
            {
                data.lastSelectedSlotIndex = -1;
            }

            // 데이터 로드는 성공했지만 구버전 파일의 재기록만 실패한 경우다.
            // 정상 데이터를 손상 파일로 격리하지 않는다.
            if (needsRewrite && !TrySave(data, out error))
            {
                data = null;
                failure = GameSettingsLoadFailure.IoFailure;
                return false;
            }

            failure = GameSettingsLoadFailure.None;
            return true;
        }

        public bool TryDelete(out string error)
        {
            return fileStore.TryDelete(out error);
        }

        public bool TryQuarantineCorrupted(out string backupPath,out string error)
        {
            backupPath = null;
            error = null;

            if (!File.Exists(SavePath))
            {
                error = "격리할 손상 설정 파일이 없습니다.";
                return false;
            }

            string directory = Path.GetDirectoryName(SavePath);
            string fileName = $"settings.corrupt.{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.json";
            backupPath = Path.Combine(directory ?? string.Empty,fileName);

            try
            {
                File.Move(SavePath, backupPath);
                TrimCorruptedBackups(directory);
                return true;
            }
            catch (Exception exception)
            {
                backupPath = null;
                error = $"손상 설정 파일을 격리할 수 없습니다: {exception.Message}";
                return false;
            }
        }
        private static void TrimCorruptedBackups(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                FileInfo[] backups = new DirectoryInfo(directory)
            .GetFiles("settings.corrupt.*.json")
            .OrderByDescending(file => file.Name)
            .ToArray();

                foreach (FileInfo backup in backups.Skip(MaxCorruptedBackupCount))
                {
                    try
                    {
                        backup.Delete();
                    }
                    catch (IOException)
                    {
                        // 백업 정리 실패가 설정 복구를 막으면 안 된다.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // 백업 정리 실패가 설정 복구를 막으면 안 된다.
                    }
                }
            }
            catch (IOException)
            {
                // 백업 목록 조회 실패가 설정 복구를 막으면 안 된다.
            }
            catch (UnauthorizedAccessException)
            {
                // 백업 목록 조회 실패가 설정 복구를 막으면 안 된다.
            }
        }
    }
}
