using System;
using UnityEngine;

namespace NorthLand.Core
{
    /// <summary>
    /// 플레이어 슬롯 선택 상태를 씬 전환 이후에도 유지한다.
    /// </summary>
    public sealed class PlayerSaveService : MonoBehaviour
    {
        public static PlayerSaveService Instance
        {
            get;
            private set;
        }

        private PlayerSlotManager slotManager;

        private const string SelectedSlotKey = "NorthLand.SelectedPlayerSlot";

        public PlayerData CurrentPlayerData
        {
            get;
            private set;
        }

        public bool HasSelectedSlot => slotManager != null && slotManager.HasSelectedSlot;

        public int CurrentSlotIndex => slotManager != null? slotManager.CurrentSlotIndex : -1;

        public string CurrentSlotPath => slotManager.CurrentSlotPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var gameObject = new GameObject(nameof(PlayerSaveService));

            gameObject.AddComponent<PlayerSaveService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            DontDestroyOnLoad(gameObject);

            slotManager =new PlayerSlotManager(Application.persistentDataPath);

            RestoreSelectedSlot();
        }

        public bool SlotExists(int slotIndex)
        {
            return slotManager.SlotExists(slotIndex);
        }

        public bool TryCreateAndSelectSlot(int slotIndex,string playerName,out string error)
        {
            if (!slotManager.TryCreateAndSelectSlot(
                    slotIndex,
                    playerName,
                    out PlayerData data,
                    out error))
            {
                return false;
            }

            CurrentPlayerData = data;
            SaveSelectedSlot(slotIndex);

            return true;
        }

        public bool TrySelectSlot(int slotIndex,out string error)
        {
            if (!slotManager.TrySelectSlot(slotIndex,out PlayerData data,out error))
            {
                return false;
            }

            CurrentPlayerData = data;
            SaveSelectedSlot(slotIndex);

            return true;
        }

        /// <summary>
        /// 슬롯을 선택하지 않고 플레이어 데이터만 읽는다.
        /// 슬롯 카드의 정보를 표시할 때 사용한다.
        /// </summary>
        public bool TryGetSlotData(int slotIndex,out PlayerData data,out string error)
        {
            return slotManager.TryLoadSlot(slotIndex,out data,out error);
        }

        /// <summary>
        /// 슬롯 폴더 전체를 삭제하고 선택 상태를 정리한다.
        /// </summary>
        public bool TryDeleteSlot(int slotIndex, out string error)
        {
            bool wasSelected = HasSelectedSlot && CurrentSlotIndex == slotIndex;

            if (!slotManager.TryDeleteSlot(slotIndex, out error))
            {
                return false;
            }

            if (wasSelected)
            {
                CurrentPlayerData = null;
            }

            if (PlayerPrefs.HasKey(SelectedSlotKey) && PlayerPrefs.GetInt(SelectedSlotKey, -1) == slotIndex)
            {
                PlayerPrefs.DeleteKey(SelectedSlotKey);
                PlayerPrefs.Save();
            }

            return true;
        }


        private static void SaveSelectedSlot(int slotIndex)
        {
            PlayerPrefs.SetInt(SelectedSlotKey,slotIndex);

            PlayerPrefs.Save();
        }

        private void RestoreSelectedSlot()
        {
            if (!PlayerPrefs.HasKey(SelectedSlotKey))
            {
                return;
            }

            int slotIndex = PlayerPrefs.GetInt(SelectedSlotKey);

            if (slotManager.TrySelectSlot(slotIndex,out PlayerData data,out _))
            {
                CurrentPlayerData = data;
                return;
            }

            // 저장된 슬롯이 삭제됐거나 잘못된 경우
            PlayerPrefs.DeleteKey(SelectedSlotKey);
            PlayerPrefs.Save();
        }

    


    }
}