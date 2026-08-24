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

        /// <summary>
        /// 슬롯 버튼에서 0, 1, 2를 전달한다.
        /// 빈 슬롯이면 생성하고, 기존 슬롯이면 불러온다.
        /// </summary>
        public void OnClickSlot(int slotIndex)
        {
            ClearError();

            PlayerSaveService service = PlayerSaveService.Instance;

            if (service == null)
            {
                ShowError("플레이어 저장 시스템이 준비되지 않았습니다.");
                return;
            }

            bool slotExists = service.SlotExists(slotIndex);

            bool success;
            string error;

            if (slotExists)
            {
                // 기존 슬롯 선택
                success = service.TrySelectSlot(slotIndex, out error);
            }
            else
            {
                // 빈 슬롯 생성
                success = service.TryCreateAndSelectSlot(slotIndex,out error);
            }

            if (!success)
            {
                if (slotExists)
                {
                    ShowError($"슬롯 {slotIndex + 1}을 불러올 수 없습니다.\n삭제 후 다시 생성해주세요. ({error})");

                    RefreshAllSlots();
                }
                else
                {
                    ShowError(error);
                }

                return;
            }

            // TrySelectSlot 또는 TryCreateAndSelectSlot 안에서
            // CurrentSlotIndex 갱신과 SelectedSlotChanged 발생이 이미 완료된다.
            CloseSlotPanel();
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

            PlayerSaveService playerSaveService = PlayerSaveService.Instance;

            if (playerSaveService != null)
            {
                playerSaveService.SelectedSlotChanged += HandleSelectedSlotChanged;
            }

            RefreshAllSlots();
        }

        private void OnDisable()
        {
            LocalizationSettings.SelectedLocaleChanged -= HandleSelectedLocaleChanged;

            PlayerSaveService playerSaveService = PlayerSaveService.Instance;

            if (playerSaveService != null)
            {
                playerSaveService.SelectedSlotChanged -= HandleSelectedSlotChanged;
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
            ClearError();

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

            if (!service.TryDeleteSlot(slotIndex,out string error))
            {
                ShowError(error);
                return;
            }

            RefreshAllSlots();
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
    }
}
