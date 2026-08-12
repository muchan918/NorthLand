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

        private LegacyRunSaveLocationMigrator legacyRunSaveLocationMigrator;

        private PlayerSlotManager slotManager;

        public PlayerData CurrentPlayerData
        {
            get;
            private set;
        }

        public event Action SelectedSlotChanged;

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

            legacyRunSaveLocationMigrator =new LegacyRunSaveLocationMigrator(Application.persistentDataPath);
        }

        private void Start()
        {
            RestoreSelectedSlot();
        }

        public bool SlotExists(int slotIndex)
        {
            return slotManager.SlotExists(slotIndex);
        }

        public bool TryCreateAndSelectSlot(int slotIndex,out string error)
        {
            if (!slotManager.TryCreateAndSelectSlot(slotIndex,out PlayerData data,out error))
            {
                return false;
            }

            CurrentPlayerData = data;
            TryMigrateLegacyRunSave();
            SaveSelectedSlot(slotIndex);
            SelectedSlotChanged?.Invoke();

            return true;
        }

        public bool TrySelectSlot(int slotIndex,out string error)
        {
            if (!slotManager.TrySelectSlot(slotIndex,out PlayerData data,out error))
            {
                return false;
            }

            CurrentPlayerData = data;
            TryMigrateLegacyRunSave();
            SaveSelectedSlot(slotIndex);
            SelectedSlotChanged?.Invoke();

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

            GameSettingsService settingsService =GameSettingsService.Instance;

            if (settingsService != null &&settingsService.CurrentSettings != null &&settingsService.CurrentSettings.lastSelectedSlotIndex == slotIndex)
            {
                if (!settingsService.TrySetLastSelectedSlotIndex(-1,out string settingsError))
                {
                    Debug.LogWarning($"[PlayerSaveService] 선택 슬롯 초기화 실패: {settingsError}",this);
                }
            }

            if (wasSelected)
            {
                SelectedSlotChanged?.Invoke();
            }

            return true;
        }


        private void SaveSelectedSlot(int slotIndex)
        {
            GameSettingsService settingsService = GameSettingsService.Instance;

            if (settingsService == null)
            {
                Debug.LogWarning("[PlayerSaveService] 게임 설정 시스템이 준비되지 않았습니다.",this);

                return;
            }

            if (!settingsService.TrySetLastSelectedSlotIndex(slotIndex,out string error))
            {
                Debug.LogWarning($"[PlayerSaveService] 선택 슬롯 저장 실패: {error}",this);
            }
        }

        private void RestoreSelectedSlot()
        {
            GameSettingsService settingsService = GameSettingsService.Instance;

            if (settingsService == null ||settingsService.CurrentSettings == null)
            {
                Debug.LogWarning("[PlayerSaveService] 게임 설정을 불러오지 못해 마지막 슬롯을 복원하지 않았습니다.",this);

                return;
            }

            int slotIndex = settingsService.CurrentSettings.lastSelectedSlotIndex;

            if (slotIndex < 0)
            {
                return;
            }

            if (slotManager.TrySelectSlot(slotIndex, out PlayerData data, out _))
            {
                CurrentPlayerData = data;

                TryMigrateLegacyRunSave();

                SelectedSlotChanged?.Invoke();

                return;
            }

            // 저장된 슬롯이 삭제됐거나 잘못된 경우
            if (!settingsService.TrySetLastSelectedSlotIndex(-1,out string error))
            {
                Debug.LogWarning($"[PlayerSaveService] 잘못된 선택 슬롯 초기화 실패: {error}",this);
            }

        }

        public bool TryUpdateLastPlayedAt(out string error)
        {
            error = null;

            if (!HasSelectedSlot ||CurrentPlayerData == null)
            {
                error = "선택된 플레이어 세이브 슬롯이 없습니다.";

                return false;
            }

            long previousTime = CurrentPlayerData.lastPlayedAt;

            CurrentPlayerData.UpdateLastPlayedAt();

            var store = new PlayerDataStore(CurrentSlotPath);

            if (!store.TrySave(CurrentPlayerData,out error))
            {
                CurrentPlayerData.lastPlayedAt = previousTime;

                return false;
            }

            return true;
        }

        private void TryMigrateLegacyRunSave()
        {
            if (!HasSelectedSlot)
            {
                return;
            }

            if (legacyRunSaveLocationMigrator == null)
            {
                Debug.LogWarning("[PlayerSaveService] 구버전 Run 세이브 이전 시스템이 준비되지 않았습니다.",this);

                return;
            }

            if (!legacyRunSaveLocationMigrator.TryMigrate(CurrentSlotPath,out bool migrated,out string error))
            {
                Debug.LogWarning($"[PlayerSaveService] 구버전 Run 세이브 이전 실패: {error}",this);

                return;
            }

            if (migrated)
            {
                Debug.Log($"[PlayerSaveService] 구버전 Run 세이브를 슬롯 {CurrentSlotIndex + 1}로 이전했습니다.",this);
            }
        }

    }
}