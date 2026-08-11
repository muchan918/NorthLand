using System;
using System.IO;

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

        public bool TryCreateAndSelectSlot(int slotIndex,string playerName,out PlayerData data,out string error)
        {
            data = null;
            error = null;

            if (!IsValidSlotIndex(slotIndex))
            {
                error = "올바르지 않은 슬롯 번호입니다.";
                return false;
            }

            var store = new PlayerDataStore(GetSlotPath(slotIndex));

            if (store.Exists)
            {
                error = "이미 사용 중인 슬롯입니다.";
                return false;
            }

            try
            {
                data = PlayerData.Create(playerName);
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }

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

            if (!TryLoadSlot(slotIndex,out data,out error))
            {
                return false;
            }

            CurrentSlotIndex = slotIndex;

            return true;
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
    }
}