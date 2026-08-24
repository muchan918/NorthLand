using Cysharp.Threading.Tasks;
using System;
using System.Threading;
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

        private bool isInitialized;
        public bool IsInitialized => isInitialized;

        public event Action Initialized;

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
            RestoreSelectedSlotAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid RestoreSelectedSlotAsync(CancellationToken cancellationToken)
        {
            try
            {
                GameSettingsService settingsService = GameSettingsService.Instance;

                if (settingsService == null || settingsService.CurrentSettings == null)
                {
                    Debug.LogWarning("[PlayerSaveService] 게임 설정을 불러오지 못해 마지막 슬롯을 복원하지 않았습니다.",this);

                    return;
                }

                int slotIndex = settingsService.CurrentSettings.lastSelectedSlotIndex;

                if (slotIndex < 0)
                {
                    return;
                }

                SaveResult<PlayerData> result = await slotManager.SelectSlotAsync(slotIndex,cancellationToken);

                if (result.Success)
                {
                    CurrentPlayerData = result.Value;

                    // 다음 마이그레이션 단계 전까지는 기존 동기 API 유지
                    TryMigrateLegacyRunSave();

                    SelectedSlotChanged?.Invoke();
                    return;
                }

                // 저장된 슬롯이 삭제됐거나 손상된 경우
                if (!settingsService.TrySetLastSelectedSlotIndex(-1,out string error))
                {
                    Debug.LogWarning($"[PlayerSaveService] 잘못된 선택 슬롯 초기화 실패: {error}",this);
                }
            }
            catch (OperationCanceledException)
            {
                // 서비스 파괴 또는 애플리케이션 종료에 따른 정상 취소
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                isInitialized = true;
                Initialized?.Invoke();
            }
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

        public async UniTask<SaveResult> UpdateLastPlayedAtAsync(CancellationToken cancellationToken)
        {
            if (!HasSelectedSlot || CurrentPlayerData == null)
            {
                return SaveResult.Failed("선택된 플레이어 세이브 슬롯이 없습니다.");
            }

            PlayerData playerData = CurrentPlayerData;
            long previousTime = playerData.lastPlayedAt;

            playerData.UpdateLastPlayedAt();

            var store = new PlayerDataStore(CurrentSlotPath);

            try
            {
                SaveResult result =await store.SaveAsync(playerData,cancellationToken);

                if (!result.Success)
                {
                    playerData.lastPlayedAt = previousTime;
                    return result;
                }

                return SaveResult.Succeeded();
            }
            catch (OperationCanceledException)
            {
                playerData.lastPlayedAt = previousTime;
                throw;
            }
        }
        public UniTask<SaveResult<PlayerData>> GetSlotDataAsync(int slotIndex,CancellationToken cancellationToken)
        {
            if (slotManager == null)
            {
                return UniTask.FromResult(SaveResult<PlayerData>.Failed("플레이어 슬롯 시스템이 준비되지 않았습니다."));
            }

            return slotManager.LoadSlotAsync(slotIndex,cancellationToken);
        }

        public async UniTask<SaveResult> CreateAndSelectSlotAsync(int slotIndex,CancellationToken cancellationToken)
        {
            if (!isInitialized)
            {
                return SaveResult.Failed("플레이어 슬롯을 초기화하는 중입니다.");
            }

            SaveResult<PlayerData> result =await slotManager.CreateAndSelectSlotAsync(slotIndex,cancellationToken);

            if (!result.Success)
            {
                return SaveResult.Failed(result.Error);
            }

            CurrentPlayerData = result.Value;

            TryMigrateLegacyRunSave();
            SaveSelectedSlot(slotIndex);
            SelectedSlotChanged?.Invoke();

            return SaveResult.Succeeded();
        }

        public async UniTask<SaveResult>SelectSlotAsync(int slotIndex,CancellationToken cancellationToken)
        {
            if (!isInitialized)
            {
                return SaveResult.Failed("플레이어 슬롯을 초기화하는 중입니다.");
            }

            SaveResult<PlayerData> result =await slotManager.SelectSlotAsync(slotIndex,cancellationToken);

            if (!result.Success)
            {
                return SaveResult.Failed(result.Error);
            }

            CurrentPlayerData = result.Value;

            TryMigrateLegacyRunSave();
            SaveSelectedSlot(slotIndex);
            SelectedSlotChanged?.Invoke();

            return SaveResult.Succeeded();
        }

        public async UniTask<SaveResult> DeleteSlotAsync(int slotIndex,CancellationToken cancellationToken)
        {
            if (!isInitialized)
            {
                return SaveResult.Failed("플레이어 슬롯을 초기화하는 중입니다.");
            }

            bool wasSelected = HasSelectedSlot &&CurrentSlotIndex == slotIndex;

            SaveResult result = await slotManager.DeleteSlotAsync(slotIndex,cancellationToken);

            if (!result.Success)
            {
                return result;
            }

            if (wasSelected)
            {
                CurrentPlayerData = null;
            }

            GameSettingsService settingsService = GameSettingsService.Instance;

            if (wasSelected && settingsService != null && settingsService.CurrentSettings != null && settingsService.CurrentSettings.lastSelectedSlotIndex == slotIndex)
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

            return SaveResult.Succeeded();
        }
    }
}