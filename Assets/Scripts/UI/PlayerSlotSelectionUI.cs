using NorthLand.Core;
using TMPro;
using UnityEngine;

namespace NorthLand.UI
{
    public sealed class PlayerSlotSelectionUI : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text selectedSlotText;

        [SerializeField]
        private TMP_Text errorText;

        [SerializeField]
        private PlayerSlotView[] slotViews;

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
                string playerName = $"세이브데이터{slotIndex + 1}";

                success =service.TryCreateAndSelectSlot(slotIndex,playerName,out error);
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

            RefreshAllSlots();
            RefreshSelectedSlot();
        }

        private void RefreshSelectedSlot()
        {
            if (selectedSlotText == null)
            {
                return;
            }

            PlayerSaveService service = PlayerSaveService.Instance;

            if (service == null || !service.HasSelectedSlot || service.CurrentPlayerData == null)
            {
                selectedSlotText.text = string.Empty;
                return;
            }

            selectedSlotText.text = service.CurrentPlayerData.playerName;
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
            RefreshAllSlots();
            RefreshSelectedSlot();
        }

        private void RefreshAllSlots()
        {
            PlayerSaveService service = PlayerSaveService.Instance;

            if (service == null || slotViews == null)
            {
                return;
            }

            foreach (PlayerSlotView slotView in slotViews)
            {
                if (slotView == null)
                {
                    continue;
                }

                int slotIndex = slotView.SlotIndex;

                bool isSelected = service.HasSelectedSlot && service.CurrentSlotIndex == slotIndex;

                if (!service.SlotExists(slotIndex))
                {
                    slotView.ShowEmpty(isSelected);
                    continue;
                }
                if (service.TryGetSlotData(slotIndex,out PlayerData data,out _))
                {
                    slotView.ShowData(data, isSelected);
                }
                else
                {
                    slotView.ShowCorrupted(isSelected);
                }
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
                RefreshSelectedSlot();
                return;
            }

            if (!service.TryDeleteSlot(slotIndex,out string error))
            {
                ShowError(error);
                return;
            }

            RefreshAllSlots();
            RefreshSelectedSlot();
        }
    }
}