using TMPro;
using UnityEngine;
using NorthLand.Core;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NorthLand.UI
{
    public class MainMenuUI : MonoBehaviour
    {
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

        private void Awake()
        {
            runSaveLoader = new RunSaveLoader();
            RefreshContinueButtonAsync().Forget();
        }

        // 랜덤 시드로 시작
        public void OnClickStart()
        {
            if (!EnsurePlayerSlotSelected())
            {
                return;
            }

            if (!TryGetSceneManager(out GameSceneManager sceneManager))
            {
                return;
            }

            sceneManager.LoadManageSpace();
        }

        // 플레이어가 입력한 시드로 시작
        public void OnClickStartWithSeed()
        {
            ClearSeedError();

            if (!EnsurePlayerSlotSelected())
            {
                return;
            }

            if (!TryGetSceneManager(out GameSceneManager sceneManager))
            {
                return;
            }

            if (seedInputField == null)
            {
                ShowSeedError("시드 입력창이 연결되지 않았습니다.");

                return;
            }

            string input = seedInputField.text.Trim();

            if (!int.TryParse(input,out int masterSeed) ||masterSeed <= 0)
            {
                ShowSeedError("1~2147483647 사이의 숫자를 입력하세요.");

                return;
            }

            sceneManager.LoadManageSpaceWithSeed(masterSeed);
        }

        private bool TryGetSceneManager(out GameSceneManager sceneManager)
        {
            sceneManager = GameSceneManager.Instance;

            if (sceneManager != null)
            {
                return true;
            }

            Debug.LogError("[MainMenuUI] GameSceneManager 인스턴스가 없습니다.");

            ShowSeedError("게임을 시작할 수 없습니다.");

            return false;
        }

        private void ShowSeedError(string message)
        {
            if (seedErrorText != null)
            {
                seedErrorText.text = message;
            }

            Debug.LogWarning($"[MainMenuUI] {message}",this);
        }

        private void ClearSeedError()
        {
            if (seedErrorText != null)
            {
                seedErrorText.text = string.Empty;
            }
        }
        private void Update()
        {
            // 타이틀 씬 전용 입력이다.
            if (Keyboard.current == null)
            {
                return;
            }

            if (!Keyboard.current.escapeKey.wasPressedThisFrame)
            {
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
        private async UniTaskVoid RefreshContinueButtonAsync()
        {
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
                return;
            }

            CancellationTokenSource refreshCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());
            continueRefreshCts = refreshCts;

            string slotPath = playerSaveService.CurrentSlotPath;

            try
            {
                SaveResult<RunData> result = await runSaveLoader.LoadAsync(
                    slotPath,
                    refreshCts.Token);

                if (playerSaveService.HasSelectedSlot &&
                    playerSaveService.CurrentSlotPath == slotPath)
                {
                    continueButton.gameObject.SetActive(result.Success);
                }
            }
            catch (OperationCanceledException)
            {
                // 슬롯 변경 또는 타이틀 화면 종료에 따른 정상 취소
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

                if (!sceneManager.TryPrepareContinue(loadResult.Value,out string prepareError))
                {
                    HideContinueButton(prepareError);
                    return;
                }

                if (!sceneManager.TryLoadContinue(out string loadError))
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
    }
}
