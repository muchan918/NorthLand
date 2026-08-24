using NorthLand.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace NorthLand.UI
{
    public sealed class PlayerSlotSelectionUI : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text errorText;

        [SerializeField]
        private PlayerSlotView[] slotViews;

        [SerializeField]
        private GameObject slotPanel;

        private CancellationTokenSource refreshCancellation;

        private bool isProcessingSlot;

        /// <summary>
        /// 슬롯 버튼에서 0, 1, 2를 전달한다.
        /// 빈 슬롯이면 생성하고, 기존 슬롯이면 불러온다.
        /// </summary>
        public void OnClickSlot(int slotIndex)
        {
            SelectSlotAsync(slotIndex).Forget();
        }

        private async UniTaskVoid SelectSlotAsync(int slotIndex)
        {
            if (isProcessingSlot)
            {
                return;
            }

            isProcessingSlot = true;
            ClearError();

            try
            {
                PlayerSaveService service = PlayerSaveService.Instance;

                if (service == null)
                {
                    ShowError("플레이어 저장 시스템이 준비되지 않았습니다.");

                    return;
                }

                CancellationToken cancellationToken = this.GetCancellationTokenOnDestroy();

                bool slotExists = service.SlotExists(slotIndex);

                SaveResult result;

                if (slotExists)
                {
                    result = await service.SelectSlotAsync(slotIndex,cancellationToken);
                }
                else
                {
                    result = await service.CreateAndSelectSlotAsync(slotIndex,cancellationToken);
                }

                if (!result.Success)
                {
                    if (slotExists)
                    {
                        ShowError($"슬롯 {slotIndex + 1}을 불러올 수 없습니다.\n삭제 후 다시 생성해주세요. ({result.Error})");

                        RefreshAllSlots();
                    }
                    else
                    {
                        ShowError(result.Error);
                    }

                    return;
                }

                CloseSlotPanel();
            }
            catch (OperationCanceledException)
            {
                // UI 파괴에 따른 정상 취소
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                ShowError("슬롯 처리 중 오류가 발생했습니다.");
            }
            finally
            {
                isProcessingSlot = false;
            }
        }

        private void ClearError()
        {
            if (errorText != null)
            {
                errorText.text = string.Empty;
            }
        }


        private void ShowError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "슬롯을 처리하지 못했습니다.";
            }

            if (errorText != null)
            {
                errorText.text = message;
            }

            Debug.LogWarning($"[PlayerSlotSelectionUI] {message}",this);
        }

        private void OnEnable()
        {
            LocalizationSettings.SelectedLocaleChanged += HandleSelectedLocaleChanged;

            PlayerSaveService service = PlayerSaveService.Instance;

            if (service != null)
            {
                service.SelectedSlotChanged += HandleSelectedSlotChanged;

                if (!service.IsInitialized)
                {
                    service.Initialized += HandlePlayerSaveInitialized;
                }
            }

            RefreshAllSlots();
        }

        private void HandlePlayerSaveInitialized()
        {
            PlayerSaveService service = PlayerSaveService.Instance;

            if (service != null)
            {
                service.Initialized -= HandlePlayerSaveInitialized;
            }

            RefreshAllSlots();
        }

        private void OnDisable()
        {
            LocalizationSettings.SelectedLocaleChanged -= HandleSelectedLocaleChanged;

            PlayerSaveService service = PlayerSaveService.Instance;

            if (service != null)
            {
                service.SelectedSlotChanged -= HandleSelectedSlotChanged;

                service.Initialized -= HandlePlayerSaveInitialized;
            }

            refreshCancellation?.Cancel();
            refreshCancellation?.Dispose();
            refreshCancellation = null;
        }

        private void HandleSelectedLocaleChanged(Locale locale)
        {
            RefreshAllSlots();
        }

        private void HandleSelectedSlotChanged()
        {
            RefreshAllSlots();
        }

        private void RefreshAllSlots()
        {
            refreshCancellation?.Cancel();
            refreshCancellation?.Dispose();

            refreshCancellation =CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

            RefreshAllSlotsAsync(refreshCancellation.Token).Forget();
        }

        private async UniTaskVoid RefreshAllSlotsAsync(CancellationToken cancellationToken)
        {
            try
            {
                PlayerSaveService service = PlayerSaveService.Instance;

                if (service == null || slotViews == null)
                {
                    return;
                }

                foreach (PlayerSlotView slotView in slotViews)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (slotView == null)
                    {
                        continue;
                    }

                    int slotIndex = slotView.SlotIndex;

                    bool isSelected =service.HasSelectedSlot &&service.CurrentSlotIndex == slotIndex;

                    if (!service.SlotExists(slotIndex))
                    {
                        slotView.ShowEmpty(isSelected);
                        continue;
                    }

                    SaveResult<PlayerData> result =await service.GetSlotDataAsync(slotIndex,cancellationToken);

                    if (result.Success)
                    {
                        slotView.ShowData(result.Value,isSelected);
                    }
                    else
                    {
                        slotView.ShowCorrupted(isSelected);

                        Debug.LogWarning($"[PlayerSlotSelectionUI] 슬롯 {slotIndex + 1}을 읽을 수 없습니다: {result.Error}",this);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 새 갱신 시작, UI 비활성화 또는 파괴에 따른 정상 취소
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// 지정한 플레이어 슬롯을 삭제하고 카드 화면을 갱신한다.
        /// </summary>
        public void OnClickDeleteSlot(int slotIndex)
        {
            DeleteSlotAsync(slotIndex).Forget();
        }

        private void CloseSlotPanel()
        {
            if (slotPanel == null)
            {
                Debug.LogWarning("[PlayerSlotSelectionUI] 슬롯 패널이 연결되지 않았습니다.",this);

                return;
            }

            slotPanel.SetActive(false);
        }

        private async UniTaskVoid DeleteSlotAsync(int slotIndex)
        {
            if (isProcessingSlot)
            {
                return;
            }

            isProcessingSlot = true;
            ClearError();

            try
            {
                PlayerSaveService service = PlayerSaveService.Instance;

                if (service == null)
                {
                    ShowError("플레이어 저장 시스템이 준비되지 않았습니다.");

                    return;
                }

                if (!service.SlotExists(slotIndex))
                {
                    RefreshAllSlots();
                    return;
                }

                SaveResult result = await service.DeleteSlotAsync(slotIndex,this.GetCancellationTokenOnDestroy());

                if (!result.Success)
                {
                    ShowError(result.Error);
                    return;
                }

                RefreshAllSlots();
            }
            catch (OperationCanceledException)
            {
                // 삭제 시작 전에 UI가 파괴된 경우의 정상 취소
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                ShowError("슬롯 삭제 중 오류가 발생했습니다.");
            }
            finally
            {
                isProcessingSlot = false;
            }
        }
    }
}
