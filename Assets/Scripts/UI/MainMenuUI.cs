using TMPro;
using UnityEngine;
using NorthLand.Core;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Localization.Components;

namespace NorthLand.UI
{
    public class MainMenuUI : MonoBehaviour
    {
        private enum NewGameRequestType
        {
            None,
            Random,
            Seed
        }

        private string pendingSlotPath;

        private NewGameRequestType pendingNewGameRequest;
        private int pendingMasterSeed;
        [Header("Seed")]

        [SerializeField]
        private TMP_InputField seedInputField;

        [SerializeField]
        private TMP_Text seedErrorText;


        [SerializeField]
        private GameObject seedGamePanle;

        [Header("Continue")]

        [SerializeField]
        private Button continueButton;


        [SerializeField]
        private GameObject savePanel;

        private RunSaveLoader runSaveLoader;
        private CancellationTokenSource continueRefreshCts;

        private bool hasValidRunSave;
        private bool isRunSaveCheckCompleted;

        [SerializeField]
        private GameObject QuitGameWarningPanel;

        [Header("New Game Warning")]

        [SerializeField]
        private GameObject newGameWarningPanel;

        [SerializeField]
        private Button newGameConfirmButton;

        [SerializeField]
        private LocalizeStringEvent seedErrorLocalize;

        private void Awake()
        {
            runSaveLoader = new RunSaveLoader();
            RefreshContinueButtonAsync().Forget();
        }

        // 랜덤 시드로 시작
        public void OnClickStart()
        {
            RequestNewGame(NewGameRequestType.Random);
        }

        private void StartRandomGame()
        {
            if (!TryGetSceneManager(out GameSceneManager sceneManager))
            {
                return;
            }

            PlayerSaveService saveService = PlayerSaveService.Instance;

            if (saveService != null && !saveService.IsTutorialCompleted)
            {
                sceneManager.LoadTutorial();
            }
            else
            {
                sceneManager.LoadManageSpace();
            }
        }
        private void StartSeedGame(int masterSeed)
        {
            if (!TryGetSceneManager(out GameSceneManager sceneManager))
            {
                return;
            }

            PlayerSaveService saveService = PlayerSaveService.Instance;

            if (saveService != null && !saveService.IsTutorialCompleted)
            {
                sceneManager.LoadTutorialWithSeed(masterSeed);
            }
            else
            {
                sceneManager.LoadManageSpaceWithSeed(masterSeed);
            }
        }

        private void RequestNewGame(
       NewGameRequestType requestType,
       int masterSeed = 0)
        {
            RequestNewGameAsync(requestType, masterSeed).Forget();
        }

        private async UniTask RequestNewGameAsync(NewGameRequestType requestType,int masterSeed)
        {
            if (!EnsurePlayerSlotSelected())
            {
                return;
            }

            if (!isRunSaveCheckCompleted)
            {
                await RefreshContinueButtonAsync();

                if (!isRunSaveCheckCompleted)
                {
                    Debug.LogWarning("[MainMenuUI] 저장 데이터 확인을 완료하지 못했습니다.",this);

                    return;
                }
            }

            PlayerSaveService playerSaveService = PlayerSaveService.Instance;

            if (playerSaveService == null || !playerSaveService.HasSelectedSlot)
            {
                return;
            }

            pendingNewGameRequest = requestType;
            pendingMasterSeed = masterSeed;
            pendingSlotPath = playerSaveService.CurrentSlotPath;

            if (hasValidRunSave)
            {
                if (newGameWarningPanel == null)
                {
                    Debug.LogError("[MainMenuUI] 새 게임 경고 패널이 연결되지 않았습니다.",this);

                    CancelPendingNewGame();
                    return;
                }

                newGameWarningPanel.SetActive(true);
                return;
            }

            StartPendingNewGame();
        }

        private void StartPendingNewGame()
        {
            NewGameRequestType requestType = pendingNewGameRequest;
            int masterSeed = pendingMasterSeed;

            pendingNewGameRequest = NewGameRequestType.None;
            pendingMasterSeed = 0;
            pendingSlotPath = null;

            switch (requestType)
            {
                case NewGameRequestType.Random:
                    StartRandomGame();
                    break;

                case NewGameRequestType.Seed:
                    StartSeedGame(masterSeed);
                    break;
            }
        }
        public void OnClickCancelNewGame()
        {
            CancelPendingNewGame();
        }

        // 플레이어가 입력한 시드로 시작
        public void OnClickStartWithSeed()
        {
            ClearSeedError();

            if (!EnsurePlayerSlotSelected())
            {
                return;
            }

            if (seedInputField == null)
            {
                ShowSeedError("title.seed.error.start_failed");
                return;
            }

            string input = seedInputField.text.Trim();

            if (!int.TryParse(input, out int masterSeed) || masterSeed <= 0)
            {
                ShowSeedError("title.seed.error.invalid");
                return;
            }

            RequestNewGame(NewGameRequestType.Seed, masterSeed);
        }

        private bool TryGetSceneManager(out GameSceneManager sceneManager)
        {
            sceneManager = GameSceneManager.Instance;

            if (sceneManager != null)
            {
                return true;
            }

            Debug.LogError("[MainMenuUI] GameSceneManager 인스턴스가 없습니다.");

            ShowSeedError("title.seed.error.start_failed");

            return false;
        }

        private void ShowSeedError(string entryKey)
        {
            if (seedErrorLocalize == null)
            {
                Debug.LogError("[MainMenuUI] SeedErrorText 로컬라이즈 컴포넌트가 연결되지 않았습니다.",this);

                return;
            }

            seedErrorLocalize.StringReference.TableReference = "NorthLand_default";

            seedErrorLocalize.StringReference.TableEntryReference = entryKey;

            seedErrorLocalize.enabled = true;
            seedErrorLocalize.RefreshString();
        }

        private void ClearSeedError()
        {
            if (seedErrorLocalize != null)
            {
                seedErrorLocalize.enabled = false;
            }

            if (seedErrorText != null)
            {
                seedErrorText.text = string.Empty;
            }
        }
        private void Update()
        {
            if (Keyboard.current == null ||
                !Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            if (newGameWarningPanel != null &&
                newGameWarningPanel.activeSelf)
            {
                OnClickCancelNewGame();
                return;
            }

            ToggleSavePanel();
        }

        public void OnClickOpenseedGamePanle()
        {
            SetSeedGamePanelActive(true);
        }

        public void OnClickCloseseedGamePanle()
        {
            SetSeedGamePanelActive(false);
        }

        private void SetSeedGamePanelActive(bool isActive)
        {
            if (seedGamePanle == null)
            {
                Debug.LogError("[MainMenuUI] 시드 게임 패널이 연결되지 않았습니다.",this);

                return;
            }

            seedGamePanle.SetActive(isActive);
        }

        /// <summary>
        /// 정상적으로 읽을 수 있는 세이브가 있을 때만 이어하기 버튼을 표시한다.
        /// </summary>
        private async UniTask RefreshContinueButtonAsync()
        {
            hasValidRunSave = false;
            isRunSaveCheckCompleted = false;

            if (continueButton == null)
            {
                Debug.LogError("[MainMenuUI] 이어하기 버튼이 연결되지 않았습니다.",this);

                return;
            }

            continueButton.gameObject.SetActive(false);

            continueRefreshCts?.Cancel();
            continueRefreshCts?.Dispose();
            continueRefreshCts = null;

            PlayerSaveService playerSaveService = PlayerSaveService.Instance;

            if (playerSaveService == null || !playerSaveService.HasSelectedSlot)
            {
                isRunSaveCheckCompleted = true;
                return;
            }

            CancellationTokenSource refreshCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            continueRefreshCts = refreshCts;

            string slotPath = playerSaveService.CurrentSlotPath;

            try
            {
                SaveResult<RunData> result = await runSaveLoader.LoadAsync(slotPath,refreshCts.Token);

                if (playerSaveService.HasSelectedSlot && playerSaveService.CurrentSlotPath == slotPath)
                {
                    hasValidRunSave = result.Success;
                    isRunSaveCheckCompleted = true;

                    continueButton.gameObject.SetActive(hasValidRunSave);
                }
            }

            catch (OperationCanceledException)
            {
                // 슬롯 변경 또는 화면 종료에 따른 정상 취소
            }
            catch (Exception exception)
            {
                hasValidRunSave = false;
                isRunSaveCheckCompleted = true;

                Debug.LogException(exception, this);
            }
            finally
            {
                if (ReferenceEquals(continueRefreshCts,refreshCts))
                {
                    continueRefreshCts = null;
                }

                refreshCts.Dispose();
            }
        }

        private void OnDestroy()
        {
            continueRefreshCts?.Cancel();
            continueRefreshCts?.Dispose();
            continueRefreshCts = null;
        }

        public void OnClickContinue()
        {
            ContinueAsync().Forget();
        }

        private void OnEnable()
        {
            PlayerSaveService playerSaveService = PlayerSaveService.Instance;

            if (playerSaveService != null)
            {
                playerSaveService.SelectedSlotChanged += HandleSelectedSlotChanged;
            }
        }

        private void OnDisable()
        {
            PlayerSaveService playerSaveService = PlayerSaveService.Instance;

            if (playerSaveService != null)
            {
                playerSaveService.SelectedSlotChanged -= HandleSelectedSlotChanged;
            }
        }

        private void HandleSelectedSlotChanged()
        {
            if (pendingNewGameRequest != NewGameRequestType.None || newGameWarningPanel != null && newGameWarningPanel.activeSelf)
            {
                CancelPendingNewGame();
            }

            RefreshContinueButtonAsync().Forget();
        }

        private void ToggleSavePanel()
        {
            if (savePanel == null)
            {
                Debug.LogError("[MainMenuUI] SavePanelUI를 찾을 수 없습니다.",this);

                return;
            }

            savePanel.SetActive(!savePanel.activeSelf);
        }

        public void OnClickOpenSavePanel()
        {
            if (savePanel == null)
            {
                Debug.LogError("[MainMenuUI] SavePanelUI를 찾을 수 없습니다.",this);

                return;
            }

            savePanel.SetActive(true);
        }

        public void OnClickCloseSavePanel()
        {
            if (savePanel == null)
            {
                Debug.LogError("[MainMenuUI] SavePanelUI를 찾을 수 없습니다.",this);

                return;
            }

            savePanel.SetActive(false);
        }

        public void OnClickConfirmNewGame()
        {
            if (pendingNewGameRequest == NewGameRequestType.None)
            {
                Debug.LogWarning("[MainMenuUI] 대기 중인 새 게임 요청이 없습니다.",this);

                CloseNewGameWarningPanel();
                return;
            }

            PlayerSaveService playerSaveService = PlayerSaveService.Instance;

            if (playerSaveService == null ||!playerSaveService.HasSelectedSlot ||playerSaveService.CurrentSlotPath != pendingSlotPath)
            {
                Debug.LogWarning("[MainMenuUI] 새 게임 요청 후 선택된 슬롯이 변경되었습니다.",this);

                CancelPendingNewGame();
                return;
            }

            if (!TryGetSceneManager(out _))
            {
                return;
            }

            if (newGameConfirmButton != null)
            {
                newGameConfirmButton.interactable = false;
            }

            var fileStore = new SaveFileStore(pendingSlotPath);

            if (!fileStore.TryDelete(out string error))
            {
                Debug.LogError(
                    $"[MainMenuUI] 기존 Run 세이브 삭제에 실패했습니다: {error}",
                    this);

                if (newGameConfirmButton != null)
                {
                    newGameConfirmButton.interactable = true;
                }

                return;
            }

            hasValidRunSave = false;
            isRunSaveCheckCompleted = true;

            if (continueButton != null)
            {
                continueButton.gameObject.SetActive(false);
            }

            CloseNewGameWarningPanel();
            StartPendingNewGame();
        }

        private void CloseNewGameWarningPanel()
        {
            if (newGameWarningPanel != null)
            {
                newGameWarningPanel.SetActive(false);
            }
        }

        private void CancelPendingNewGame()
        {
            pendingNewGameRequest = NewGameRequestType.None;
            pendingMasterSeed = 0;
            pendingSlotPath = null;

            if (newGameConfirmButton != null)
            {
                newGameConfirmButton.interactable = true;
            }

            CloseNewGameWarningPanel();
        }

        private bool EnsurePlayerSlotSelected()
        {
            PlayerSaveService playerSaveService = PlayerSaveService.Instance;

            if (playerSaveService != null && playerSaveService.HasSelectedSlot)
            {
                return true;
            }

            Debug.LogWarning("[MainMenuUI] 플레이어 세이브 슬롯을 먼저 선택해야 합니다.",this);

            if (savePanel != null)
            {
                savePanel.SetActive(true);
            }

            return false;
        }
        private async UniTaskVoid ContinueAsync()
        {
            if (continueButton != null)
            {
                continueButton.interactable = false;
            }

            try
            {
                PlayerSaveService playerSaveService = PlayerSaveService.Instance;

                if (playerSaveService == null || !playerSaveService.HasSelectedSlot)
                {
                    HideContinueButton("선택된 플레이어 슬롯이 없습니다.");

                    return;
                }

                if (!TryGetSceneManager(out GameSceneManager sceneManager))
                {
                    return;
                }

                SaveResult<RunData> loadResult = await runSaveLoader.LoadAsync(
                    playerSaveService.CurrentSlotPath,
                    this.GetCancellationTokenOnDestroy());

                if (!loadResult.Success)
                {
                    HideContinueButton(loadResult.Error);
                    return;
                }

                if (!sceneManager.TryLoadContinue(loadResult.Value,out string loadError))
                {
                    HideContinueButton(loadError);
                }
            }
            catch (OperationCanceledException)
            {
                // 타이틀 화면이 닫히면서 발생한 정상 취소
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);

                HideContinueButton("이어하기 준비 중 오류가 발생했습니다.");
            }
            finally
            {
                if (continueButton != null)
                {
                    continueButton.interactable = true;
                }
            }
        }
        private void HideContinueButton(string error)
        {
            if (continueButton != null)
            {
                continueButton.gameObject.SetActive(false);
            }

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[MainMenuUI] 이어하기를 시작할 수 없습니다: {error}",this);
            }
        }

        public void OpenQuitGameWarningPanel()
        {
            if (QuitGameWarningPanel == null)
            {
                Debug.LogError($"[{QuitGameWarningPanel}] 패널이 연결되지 않았습니다.", this);
                return;
            }

            QuitGameWarningPanel.SetActive(true);
        }

        public void CloseQuitGameWarningPanel()
        {
            if (QuitGameWarningPanel == null)
            {
                Debug.LogError($"[{QuitGameWarningPanel}] 패널이 연결되지 않았습니다.", this);
                return;
            }

            QuitGameWarningPanel.SetActive(false);
        }
    }
}
