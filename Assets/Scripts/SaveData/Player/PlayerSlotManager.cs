using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NorthLand.Core
{
    /// <summary>
    /// 플레이어 세이브 슬롯의 선택 상태와 저장 경로를 관리한다.
    /// 슬롯 번호는 내부적으로 0부터 2까지 사용한다.
    /// </summary>
    public sealed class PlayerSlotManager
    {
        public const int SlotCount = 3;

        private const string SaveSlotsDirectoryName = "SaveSlots";
        private const string SlotDirectoryPrefix = "slot-";

        private readonly string saveRootPath;

        public int CurrentSlotIndex { get; private set; } = -1;

        public bool HasSelectedSlot => IsValidSlotIndex(CurrentSlotIndex);

        public string CurrentSlotPath
        {
            get
            {
                if (!HasSelectedSlot)
                {
                    throw new InvalidOperationException("선택된 플레이어 세이브 슬롯이 없습니다.");
                }

                return GetSlotPath(CurrentSlotIndex);
            }
        }

        public PlayerSlotManager(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                throw new ArgumentException("저장 루트 경로가 비어 있습니다.",nameof(persistentDataPath));
            }

            saveRootPath = Path.Combine(persistentDataPath,SaveSlotsDirectoryName);
        }

        public bool SlotExists(int slotIndex)
        {
            if (!IsValidSlotIndex(slotIndex))
            {
                return false;
            }

            var store = new PlayerDataStore(GetSlotPath(slotIndex));

            return store.Exists;
        }

        public bool TryCreateAndSelectSlot(int slotIndex,out PlayerData data,out string error)
        {
            data = null;
            error = null;

            if (!IsValidSlotIndex(slotIndex))
            {
                error = "올바르지 않은 슬롯 번호입니다.";
                return false;
            }

            PlayerDataStore store = new PlayerDataStore(GetSlotPath(slotIndex));

            if (store.Exists)
            {
                error = "이미 사용 중인 슬롯입니다.";
                return false;
            }

            data = PlayerData.Create();

            if (!store.TrySave(data, out error))
            {
                data = null;
                return false;
            }

            CurrentSlotIndex = slotIndex;

            return true;

        }

        public bool TryLoadSlot(int slotIndex,out PlayerData data,out string error)
        {
            data = null;
            error = null;

            if (!IsValidSlotIndex(slotIndex))
            {
                error = "올바르지 않은 슬롯 번호입니다.";
                return false;
            }

            var store = new PlayerDataStore(GetSlotPath(slotIndex));

            return store.TryLoad(out data,out error);
        }


        public bool TrySelectSlot(int slotIndex,out PlayerData data,out string error)
        {
            data = null;
            error = null;

            if (!IsValidSlotIndex(slotIndex))
            {
                error = "올바르지 않은 슬롯 번호입니다.";
                return false;
            }

            var store = new PlayerDataStore(GetSlotPath(slotIndex));

            if (!store.TryLoad(out data, out error))
            {
                return false;
            }

            CurrentSlotIndex = slotIndex;

            return true;
        }

        /// <summary>
        /// 플레이어 슬롯 폴더와 내부 저장 데이터를 모두 삭제한다.
        /// </summary>
        public bool TryDeleteSlot(int slotIndex,out string error)
        {
            error = null;

            if (!IsValidSlotIndex(slotIndex))
            {
                error = "올바르지 않은 슬롯 번호입니다.";
                return false;
            }

            string slotPath = GetSlotPath(slotIndex);

            try
            {
                if (Directory.Exists(slotPath))
                {
                    Directory.Delete(slotPath,recursive: true);
                }
            }
            catch (IOException exception)
            {
                error = $"슬롯 파일 삭제에 실패했습니다: " + exception.Message;

                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                error = $"슬롯 파일에 접근할 수 없습니다: " + exception.Message;

                return false;
            }

            if (CurrentSlotIndex == slotIndex)
            {
                CurrentSlotIndex = -1;
            }

            return true;
        }

        public async UniTask<SaveResult<PlayerData>> LoadSlotAsync(int slotIndex,CancellationToken cancellationToken)
        {
            if (!IsValidSlotIndex(slotIndex))
            {
                return SaveResult<PlayerData>.Failed("올바르지 않은 슬롯 번호입니다.");
            }

            var store = new PlayerDataStore(GetSlotPath(slotIndex));

            return await store.LoadAsync(cancellationToken);
        }


        public string GetSlotPath(int slotIndex)
        {
            if (!IsValidSlotIndex(slotIndex))
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex),$"슬롯 번호는 0부터 {SlotCount - 1}까지 사용할 수 있습니다.");
            }

            return Path.Combine(saveRootPath,$"{SlotDirectoryPrefix}{slotIndex}");
        }

        public static bool IsValidSlotIndex(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < SlotCount;
        }
        public async UniTask<SaveResult<PlayerData>>
        CreateAndSelectSlotAsync(int slotIndex,CancellationToken cancellationToken)
        {
            if (!IsValidSlotIndex(slotIndex))
            {
                return SaveResult<PlayerData>.Failed("올바르지 않은 슬롯 번호입니다.");
            }

            var store =new PlayerDataStore(GetSlotPath(slotIndex));

            if (store.Exists)
            {
                return SaveResult<PlayerData>.Failed("이미 사용 중인 슬롯입니다.");
            }

            PlayerData data = PlayerData.Create();

            SaveResult saveResult = await store.SaveAsync(data,cancellationToken);

            if (!saveResult.Success)
            {
                return SaveResult<PlayerData>.Failed(saveResult.Error);
            }

            cancellationToken.ThrowIfCancellationRequested();

            CurrentSlotIndex = slotIndex;

            return SaveResult<PlayerData>.Succeeded(data);
        }

        public async UniTask<SaveResult<PlayerData>> SelectSlotAsync(int slotIndex,CancellationToken cancellationToken)
        {
            if (!IsValidSlotIndex(slotIndex))
            {
                return SaveResult<PlayerData>.Failed("올바르지 않은 슬롯 번호입니다.");
            }

            var store =new PlayerDataStore(GetSlotPath(slotIndex));

            SaveResult<PlayerData> loadResult = await store.LoadAsync(cancellationToken);

            if (!loadResult.Success)
            {
                return loadResult;
            }

            cancellationToken.ThrowIfCancellationRequested();

            CurrentSlotIndex = slotIndex;

            return loadResult;
        }

        public async UniTask<SaveResult> DeleteSlotAsync(int slotIndex,CancellationToken cancellationToken)
        {
            if (!IsValidSlotIndex(slotIndex))
            {
                return SaveResult.Failed("올바르지 않은 슬롯 번호입니다.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            string slotPath = GetSlotPath(slotIndex);

            SaveResult result = await UniTask.RunOnThreadPool(() =>
                    {
                        try
                        {
                            if (Directory.Exists(slotPath))
                            {
                                Directory.Delete(slotPath,recursive: true);
                            }

                            return SaveResult.Succeeded();
                        }
                        catch (IOException exception)
                        {
                            return SaveResult.Failed($"슬롯 파일 삭제에 실패했습니다: {exception.Message}");
                        }
                        catch (UnauthorizedAccessException exception)
                        {
                            return SaveResult.Failed($"슬롯 파일에 접근할 수 없습니다: {exception.Message}");
                        }
                    },
                    cancellationToken: CancellationToken.None);

            if (!result.Success)
            {
                return result;
            }

            // 파일 삭제가 성공한 경우 메모리 상태도 반드시 맞춘다.
            // 삭제 시작 후에는 취소 토큰으로 이 상태 갱신을 건너뛰지 않는다.
            if (CurrentSlotIndex == slotIndex)
            {
                CurrentSlotIndex = -1;
            }

            return SaveResult.Succeeded();
        }
    }
}