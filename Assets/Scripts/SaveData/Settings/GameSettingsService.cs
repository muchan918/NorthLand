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
                    return;
                }

                Debug.LogWarning($"게임 설정을 불러오지 못했습니다: {error}",this);
            }

            CurrentSettings =
                GameSettingsData.CreateDefault();

            if (!store.TrySave(CurrentSettings, out string saveError))
            {
                Debug.LogWarning($"기본 게임 설정을 저장하지 못했습니다: {saveError}",this);
            }
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
    }
}