using System;
using UnityEngine;
using System.Threading;
using Cysharp.Threading.Tasks;

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

        private GameManager gameManager;

        /// <summary>
        /// 현재 세이브 파일이 존재하는지 반환한다.
        /// </summary>
        public bool HasSave =>fileStore != null && fileStore.Exists;

        private bool isSaving;
        private bool savePending;

        private bool runEnded;
        private GameResult runEndResult;

        private void Awake()
        {
            serializer = new SaveSerializer();
            PlayerSaveService playerSaveService = PlayerSaveService.Instance;

            if (playerSaveService == null || !playerSaveService.HasSelectedSlot)
            {
                Debug.LogError("[Save] 선택된 플레이어 세이브 슬롯이 없습니다.",this);

                enabled = false;
                return;
            }

            fileStore = new SaveFileStore(playerSaveService.CurrentSlotPath);

            GameSceneManager sceneManager = GameSceneManager.Instance;

            if (sceneManager == null ||!sceneManager.TryConsumeContinueData(out RunData continueData))
            {
                return;
            }

            if (TryPrepareRestore(continueData))
            {
                return;
            }

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

            gameManager = GameManager.Instance;

            if (gameManager == null)
            {
                Debug.LogError("[Save] GameManager가 준비되지 않았습니다.",this);

                return;
            }

            gameManager.OnResultDecided += HandleResultDecided;

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

                // 복원이 불가능한 세이브를 남겨두면 이어하기 실패가 반복되므로 삭제한다.
                if (fileStore == null)
                {
                    Debug.LogError("[Load] 세이브 삭제 시스템이 초기화되지 않았습니다.",this);
                }
                else if (!fileStore.TryDelete(out string deleteError))
                {
                    Debug.LogError($"[Load] 복원 불가 세이브 삭제에 실패했습니다: {deleteError}",this);
                }
                else
                {
                    Debug.Log("[Load] 복원 불가 세이브를 삭제했습니다.",this);
                }

                enabled = false;

                if (GameSceneManager.Instance != null)
                    GameSceneManager.Instance.LoadMainMenu();

                return;
            }

            Debug.Log("[Load] 전체 Run 상태 복원이 완료됐습니다.",this);
        }
        private async UniTask<SaveResult> SaveOnceAsync(CancellationToken cancellationToken)
        {
            if (runEnded)
            {
                return SaveResult.Failed("종료된 Run은 저장할 수 없습니다.");
            }

            if (isRestoring)
            {
                return SaveResult.Failed("복원 중에는 저장할 수 없습니다.");
            }

            if (serializer == null || fileStore == null)
            {
                return SaveResult.Failed("저장 시스템이 초기화되지 않았습니다.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Unity 객체 접근 구간이므로 메인 스레드에서 실행한다.
            if (!TryCaptureRunData(out RunData data))
            {
                return SaveResult.Failed("Run 상태 수집에 실패했습니다.");
            }

            string json;

            try
            {
                // 직렬화도 현재는 메인 스레드에서 실행한다.
                json = serializer.Serialize(data);
            }
            catch (Exception exception)
            {
                return SaveResult.Failed($"JSON 직렬화에 실패했습니다: {exception.Message}");
            }

            SaveResult writeResult = await fileStore.WriteAsync(json, cancellationToken);

            if (!writeResult.Success)
            {
                return writeResult;
            }

            // UniTask는 기본적으로 호출한 PlayerLoop로 돌아오지만,
            // 향후 WriteAsync 구현이 바뀌어도 안전하게 메인 스레드를 보장한다.
            await UniTask.SwitchToMainThread(cancellationToken);

            PlayerSaveService playerSaveService = PlayerSaveService.Instance;

            if (playerSaveService == null)
            {
                Debug.LogWarning("[Save] 플레이어 저장 시스템이 없어 업데이트 시간을 기록하지 못했습니다.",this);
            }
            else
            {
                SaveResult playerResult = await playerSaveService.UpdateLastPlayedAtAsync(cancellationToken);

                if (!playerResult.Success)
                {
                    Debug.LogWarning($"[Save] 플레이어 업데이트 시간 기록 실패: " +playerResult.Error,this);
                }
            }
            Debug.Log($"[Save] 저장 완료: {fileStore.SavePath}", this);

            return SaveResult.Succeeded();
        }

        private async UniTask<SaveResult> SaveNowAsync(CancellationToken cancellationToken)
        {
            if (runEnded)
            {
                return SaveResult.Failed("종료된 Run은 저장할 수 없습니다.");
            }

            // 낮 시작 이벤트가 저장 중 다시 발생하면
            // 현재 저장 완료 후 최신 상태를 한 번 더 저장한다.
            if (isSaving)
            {
                savePending = true;

                return SaveResult.Succeeded();
            }

            isSaving = true;

            SaveResult lastResult = SaveResult.Succeeded();

            try
            {
                do
                {
                    savePending = false;

                    lastResult = await SaveOnceAsync(cancellationToken);

                    if (!lastResult.Success)
                    {
                        return lastResult;
                    }
                }
                while (savePending);

                return lastResult;
            }
            finally
            {
                isSaving = false;

                if (runEnded)
                {
                    TryDeleteEndedRunSave(runEndResult);
                }
            }
        }

        private bool TryPrepareRestore(RunData data)
        {
            pendingRestoreData = null;

            if (data == null)
            {
                Debug.LogError("[Load] 복원할 RunData가 없습니다.",this);

                return false;
            }

            if (runBootstrapper == null)
            {
                Debug.LogError("[Load] RunBootstrapper가 연결되지 않았습니다.",this);

                return false;
            }

            isRestoring = true;
            suppressNextDayStartSave = true;

            if (!runBootstrapper.TryPrepareRestore(data))
            {
                isRestoring = false;
                suppressNextDayStartSave = false;

                return false;
            }

            pendingRestoreData = data;

            Debug.Log("[Load] 세이브 데이터의 시드 복원 준비가 완료됐습니다.",this);

            return true;
        }



        /// <summary>
        /// 낮이 시작되면 현재 Run 상태를 자동 저장한다.
        /// 이어하기 직후 발생하는 최초 낮 시작 이벤트는 한 번 건너뛴다.
        /// </summary>
        private void HandleDayStart()
        {
            if (TutorialMode.IsActive)
            {
                Debug.Log("[Save] 튜토리얼 중에는 낮 시작 자동 저장을 건너뜁니다.", this);
                return;
            }

            if (isRestoring)
                return;

            if (suppressNextDayStartSave)
            {
                suppressNextDayStartSave = false;

                Debug.Log("[Save] 이어하기 직후 낮 시작 자동 저장을 억제했습니다.",this);

                return;
            }

            SaveFromDayStartAsync().Forget();
        }

        private async UniTaskVoid SaveFromDayStartAsync()
        {
            try
            {
                SaveResult result = await SaveNowAsync(this.GetCancellationTokenOnDestroy());

                if (!result.Success)
                {
                    Debug.LogError($"[Save] {result.Error}", this);
                }
            }
            catch (OperationCanceledException)
            {
                // 씬 종료 또는 오브젝트 파괴에 따른 정상 취소
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// 현재 슬롯의 Run 세이브를 삭제한다.
        /// 새 튜토리얼 시작처럼 기존 진행을 명시적으로 초기화하는 경로에서 사용한다.
        /// </summary>
        public bool TryDeleteCurrentRun()
        {
            if (fileStore == null)
            {
                Debug.LogWarning(
                    "[Save] GameScene 직접 실행 상태에서는 기존 Run을 안전하게 식별할 수 없습니다. " +
                    "타이틀에서 슬롯을 선택한 뒤 튜토리얼 다시 보기를 실행해주세요.",
                    this);
                return false;
            }

            if (!fileStore.TryDelete(out string error))
            {
                Debug.LogError($"[Save] Run 세이브 삭제에 실패했습니다: {error}", this);
                return false;
            }

            Debug.Log("[Save] 새 튜토리얼 시작을 위해 기존 Run 세이브를 삭제했습니다.", this);
            return true;
        }

        private void OnDestroy()
        {
            if (dayNightManager != null)
                dayNightManager.OnDayStart -= HandleDayStart;

            if (gameManager != null)
                gameManager.OnResultDecided -= HandleResultDecided;
        }

        /// <summary>
        /// 패배 또는 승리로 Run이 종료되면 이어하기 세이브를 삭제한다.
        /// </summary>
        private void HandleResultDecided(GameResult result)
        {
            if (result == GameResult.Playing)
            {
                return;
            }

            runEnded = true;
            runEndResult = result;

            // 종료 후 추가 저장 반복을 막는다.
            savePending = false;

            // 저장 중이면 SaveNowAsync의 finally가 마지막에 삭제한다.
            if (isSaving)
            {
                return;
            }

            TryDeleteEndedRunSave(result);
        }

        private void TryDeleteEndedRunSave(GameResult result)
        {
            if (fileStore == null)
            {
                Debug.LogError("[Save] 세이브 삭제 시스템이 초기화되지 않았습니다.",this);

                return;
            }

            if (!fileStore.TryDelete(out string error))
            {
                Debug.LogError($"[Save] 종료된 Run의 세이브 삭제에 실패했습니다: {error}",this);

                return;
            }

            Debug.Log($"[Save] Run 종료({result})로 세이브를 삭제했습니다.",this);
        }

    }
}
