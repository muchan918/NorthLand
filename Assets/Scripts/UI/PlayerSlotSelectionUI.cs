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

            bool success;
            string error;

            if (service.SlotExists(slotIndex))
            {
                // 기존 슬롯 선택
                success = service.TrySelectSlot(slotIndex,out error);
            }
            else
            {
                // 빈 슬롯 생성
                string playerName = $"세이브데이터{slotIndex + 1}";

                success = service.TryCreateAndSelectSlot(slotIndex,playerName,out error);
            }

            if (!success)
            {
                ShowError(error);
                return;
            }

            RefreshAllSlots();
            RefreshSelectedSlot();
        }

        private void RefreshSelectedSlot()
        {
            PlayerSaveService service = PlayerSaveService.Instance;

            if (selectedSlotText == null ||service == null ||!service.HasSelectedSlot)
            {
                return;
            }

            PlayerData playerData = service.CurrentPlayerData;

            if (playerData == null)
            {
                return;
            }

            selectedSlotText.text = playerData.playerName;
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

                if (service.TryGetSlotData(slotIndex,out PlayerData data,out string error))
                {
                    slotView.ShowData(data, isSelected);
                }
                else
                {
                    slotView.ShowEmpty(false);
                    ShowError(error);
                }
            }
        }
    }
}