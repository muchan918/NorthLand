using System;
using UnityEngine;

namespace NorthLand.Core
{
    public sealed partial class RunSaveManager
    {
        private SaveSerializer serializer;
        private SaveFileStore fileStore;

        private bool isRestoring;

        private RunData pendingRestoreData;

        private DayNightManager dayNightManager;
        private bool suppressNextDayStartSave;

        /// <summary>
        /// 현재 세이브 파일이 존재하는지 반환한다.
        /// </summary>
        public bool HasSave =>fileStore != null && fileStore.Exists;

        private void Awake()
        {
            serializer = new SaveSerializer();

            fileStore = new SaveFileStore(
                Application.persistentDataPath);

            GameSceneManager sceneManager = GameSceneManager.Instance;

            if (sceneManager == null || !sceneManager.TryConsumeContinueRequest())
            {
                return;
            }

            if (TryPrepareRestoreFromFile())
                return;

            Debug.LogError("[Load] 이어하기 준비에 실패하여 타이틀로 돌아갑니다.",this);

            // 잘못된 상태로 신규 게임이 시작되는 것을 막는다.
            if (runBootstrapper != null)
                runBootstrapper.enabled = false;

            enabled = false;
            sceneManager.LoadMainMenu();
        }

        private void Start()
        {
            dayNightManager = DayNightManager.Instance;

            if (dayNightManager == null)
            {
                Debug.LogError("[Save] DayNightManager가 준비되지 않았습니다.",this);

                return;
            }

            dayNightManager.OnDayStart += HandleDayStart;

            if (pendingRestoreData == null)
                return;

            RunData data = pendingRestoreData;
            bool restored = false;

            try
            {
                restored = TryRestoreRunData(data);
            }
            finally
            {
                pendingRestoreData = null;
                isRestoring = false;
            }

            if (!restored)
            {
                Debug.LogError("[Load] 전체 상태 복원에 실패하여 타이틀로 돌아갑니다.",this);

                enabled = false;

                if (GameSceneManager.Instance != null)
                    GameSceneManager.Instance.LoadMainMenu();

                return;
            }

            Debug.Log("[Load] 전체 Run 상태 복원이 완료됐습니다.",this);
        }

        /// <summary>
        /// 현재 낮 시작 상태를 수집하고 단일 Run 세이브 파일에 기록한다.
        /// 수동 저장과 낮 시작 자동 저장이 공통으로 사용하는 진입점이다.
        /// </summary>
        public bool TrySaveNow()
        {
            if (isRestoring)
            {
                Debug.LogWarning("[Save] 복원 중에는 저장할 수 없습니다.",this);

                return false;
            }

            if (serializer == null || fileStore == null)
            {
                Debug.LogError("[Save] 저장 시스템이 초기화되지 않았습니다.",this);

                return false;
            }

            if (!TryCaptureRunData(out RunData data))
                return false;

            string json;

            try
            {
                json = serializer.Serialize(data);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Save] JSON 직렬화에 실패했습니다: {exception.Message}",this);

                return false;
            }

            if (!fileStore.TryWrite(json, out string error))
            {
                Debug.LogError($"[Save] {error}",this);

                return false;
            }

            Debug.Log($"[Save] 저장 완료: {fileStore.SavePath}",this);

            return true;
        }

        /// <summary>
        /// 세이브 파일을 읽고 역직렬화한 뒤,
        /// RunBootstrapper가 저장 시드로 월드를 생성할 수 있도록 데이터를 선주입한다.
        /// 반드시 RunBootstrapper.Start보다 먼저 호출해야 한다.
        /// </summary>
        private bool TryPrepareRestoreFromFile()
        {
            pendingRestoreData = null;

            if (serializer == null || fileStore == null)
            {
                Debug.LogError("[Load] 저장 시스템이 초기화되지 않았습니다.",this);

                return false;
            }

            if (runBootstrapper == null)
            {
                Debug.LogError("[Load] RunBootstrapper가 연결되지 않았습니다.",this);

                return false;
            }

            if (!fileStore.TryRead(out string json,out string readError))
            {
                Debug.LogError($"[Load] {readError}",this);

                return false;
            }

            if (!serializer.TryDeserialize(json,out RunData data,out string deserializeError))
            {
                Debug.LogError($"[Load] {deserializeError}",this);

                return false;
            }

            // 이 시점부터 자동 저장을 억제한다.
            isRestoring = true;
            suppressNextDayStartSave = true;

            if (!runBootstrapper.TryPrepareRestore(data))
            {
                isRestoring = false;
                suppressNextDayStartSave = false;

                return false;
            }

            pendingRestoreData = data;

            Debug.Log("[Load] 세이브 파일 읽기와 시드 복원 준비가 완료됐습니다.",this);

            return true;
        }

        /// <summary>
        /// 낮이 시작되면 현재 Run 상태를 자동 저장한다.
        /// 이어하기 직후 발생하는 최초 낮 시작 이벤트는 한 번 건너뛴다.
        /// </summary>
        private void HandleDayStart()
        {
            if (isRestoring)
                return;

            if (suppressNextDayStartSave)
            {
                suppressNextDayStartSave = false;

                Debug.Log("[Save] 이어하기 직후 낮 시작 자동 저장을 억제했습니다.",this);

                return;
            }

            TrySaveNow();
        }

        private void OnDestroy()
        {
            if (dayNightManager != null)
                dayNightManager.OnDayStart -= HandleDayStart;
        }

    }
}