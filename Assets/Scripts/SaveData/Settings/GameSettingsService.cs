using System;
using UnityEngine;

namespace NorthLand.Core
{
    /// <summary>
    /// 게임 공통 설정을 메모리에 보관하고
    /// settings.json으로 저장한다.
    /// </summary>
    public sealed class GameSettingsService : MonoBehaviour
    {
        public static GameSettingsService Instance
        {
            get;
            private set;
        }

        private bool canSaveSettings;

        private GameSettingsStore store;

        public GameSettingsData CurrentSettings
        {
            get;
            private set;
        }

        public event Action SettingsChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var gameObject = new GameObject(nameof(GameSettingsService));

            gameObject.AddComponent<GameSettingsService>();
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

            store = new GameSettingsStore(Application.persistentDataPath);

            LoadOrCreateSettings();
        }

        private void LoadOrCreateSettings()
        {
            if (store.Exists)
            {
                if (store.TryLoad(out GameSettingsData data,out string error))
                {
                    CurrentSettings = data;
                    canSaveSettings = true;
                    return;
                }

                Debug.LogWarning($"게임 설정을 불러오지 못했습니다: {error}",this);

                // 기존 파일은 보존하고 이번 실행에서만 기본값을 사용한다.
                CurrentSettings = GameSettingsData.CreateDefault();

                canSaveSettings = false;
                return;
            }

            CurrentSettings = GameSettingsData.CreateDefault();

            if (!store.TrySave(CurrentSettings,out string saveError))
            {
                canSaveSettings = false;

                Debug.LogWarning($"기본 게임 설정을 저장하지 못했습니다: {saveError}",this);

                return;
            }

            canSaveSettings = true;
        }

        public bool TrySetLocale(string localeCode,out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(localeCode))
            {
                error = "언어 코드가 비어 있습니다.";
                return false;
            }

            if (CurrentSettings == null)
            {
                error = "게임 설정이 준비되지 않았습니다.";
                return false;
            }

            if (!canSaveSettings)
            {
                error ="기존 설정 파일을 불러올 수 없어 설정을 저장하지 않았습니다.";

                return false;
            }

            string previousLocale = CurrentSettings.localeCode;

            CurrentSettings.localeCode = localeCode.Trim();

            if (!store.TrySave(CurrentSettings, out error))
            {
                CurrentSettings.localeCode = previousLocale;

                return false;
            }

            SettingsChanged?.Invoke();

            return true;
        }

        public bool TrySetLastSelectedSlotIndex(int slotIndex,out string error)
        {
            error = null;

            // -1은 선택 해제, 0~2는 정상 슬롯
            if (slotIndex < -1 ||slotIndex >= PlayerSlotManager.SlotCount)
            {
                error = $"슬롯 번호는 -1부터 {PlayerSlotManager.SlotCount - 1}까지 사용할 수 있습니다.";

                return false;
            }

            if (CurrentSettings == null)
            {
                error = "게임 설정이 준비되지 않았습니다.";
                return false;
            }

            if (!canSaveSettings)
            {
                error = "기존 설정 파일을 불러올 수 없어 선택 슬롯을 저장하지 않았습니다.";

                return false;
            }

            int previousSlotIndex = CurrentSettings.lastSelectedSlotIndex;

            CurrentSettings.lastSelectedSlotIndex = slotIndex;

            if (!store.TrySave(CurrentSettings, out error))
            {
                CurrentSettings.lastSelectedSlotIndex = previousSlotIndex;

                return false;
            }

            SettingsChanged?.Invoke();

            return true;
        }
    }
}