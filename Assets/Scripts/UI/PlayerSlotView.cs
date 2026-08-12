using System;
using System.Globalization;
using NorthLand.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;

namespace NorthLand.UI
{
    /// <summary>
    /// 플레이어 세이브 슬롯 카드 하나의 화면 표시를 담당한다.
    /// </summary>
    public sealed class PlayerSlotView : MonoBehaviour
    {
        [Header("Slot")]
        [SerializeField]
        [Min(0)]
        private int slotIndex;

        [Header("Text")]
        [SerializeField]
        private TMP_Text playerNameText;

        [SerializeField]
        private TMP_Text updatedAtText;

        [SerializeField]
        private TMP_Text emptyText;

        [Header("State")]
        [SerializeField]
        private GameObject selectedMarker;

        [SerializeField]
        private Button deleteButton;

        private const string SlotNameKey = "save.slot.name";
        private const string EmptyKey = "save.slot.empty";
        private const string CorruptedKey = "save.slot.corrupted";
        private const string UpdatedKey = "save.slot.updated";

        public int SlotIndex => slotIndex;

        /// <summary>
        /// 비어 있는 슬롯으로 표시한다.
        /// </summary>
        public void ShowEmpty(bool isSelected)
        {
            if (playerNameText != null)
            {
                playerNameText.text = GetSlotName();
            }

            if (updatedAtText != null)
            {
                updatedAtText.gameObject.SetActive(false);
            }

            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(true);
                emptyText.text = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable,EmptyKey);
            }

            if (selectedMarker != null)
            {
                selectedMarker.SetActive(isSelected);
            }

            if (deleteButton != null)
            {
                deleteButton.gameObject.SetActive(false);
            }
        }


        /// <summary>
        /// 파일은 존재하지만 불러올 수 없는 슬롯으로 표시한다.
        /// 삭제 후 다시 생성할 수 있도록 삭제 버튼은 유지한다.
        /// </summary>
        public void ShowCorrupted(bool isSelected)
        {
            if (playerNameText != null)
            {
                playerNameText.text = GetSlotName();
            }

            if (updatedAtText != null)
            {
                updatedAtText.gameObject.SetActive(false);
            }

            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(true);
                emptyText.text = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable,CorruptedKey);
            }

            if (selectedMarker != null)
            {
                selectedMarker.SetActive(isSelected);
            }

            if (deleteButton != null)
            {
                deleteButton.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// 저장된 플레이어 데이터로 슬롯을 표시한다.
        /// </summary>
        public void ShowData(PlayerData data, bool isSelected)
        {
            if (data == null)
            {
                ShowEmpty(isSelected);
                return;
            }

            if (playerNameText != null)
            {
                playerNameText.text = GetSlotName();
            }

            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(false);
            }

            if (updatedAtText != null)
            {
                updatedAtText.gameObject.SetActive(true);
                updatedAtText.text = FormatUpdatedAt(data.lastPlayedAt);
            }

            if (selectedMarker != null)
            {
                selectedMarker.SetActive(isSelected);
            }

            if (deleteButton != null)
            {
                deleteButton.gameObject.SetActive(true);
            }
        }

        private static string FormatUpdatedAt(long unixTimeSeconds)
        {
            DateTimeOffset dateTime =DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds).ToLocalTime();

            string localeCode =LocalizationSettings.SelectedLocale?.Identifier.Code;

            CultureInfo culture;

            try
            {
                culture = string.IsNullOrWhiteSpace(localeCode) ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo(localeCode);
            }
            catch (CultureNotFoundException)
            {
                culture = CultureInfo.CurrentCulture;
            }

            string formattedDate = dateTime.ToString("g", culture);

            return LocalizationHelper.Get(LocalizationHelper.k_DefaultTable,UpdatedKey,formattedDate);
        }
        private string GetSlotName()
        {
            return LocalizationHelper.Get(LocalizationHelper.k_DefaultTable,SlotNameKey,slotIndex + 1);
        }
    }
}