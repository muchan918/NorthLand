using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NorthLand.UI
{
    public sealed class NextWaveMonsterEntry : MonoBehaviour
    {
        private const string k_BossStringTableKey = "enemies.system.boss";

        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text countText;

        [Header("Count Colors")]
        [SerializeField] private Color normalCountColor = Color.white;
        [SerializeField] private Color bossCountColor = Color.red;

        public void Bind(Sprite icon, int count, bool isBoss)
        {
            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }

            if (countText == null)
                return;

            countText.text = isBoss? LocalizationHelper.Get(LocalizationHelper.k_EnemiesTable,k_BossStringTableKey): count.ToString();
            countText.color = isBoss ? bossCountColor: normalCountColor;
        }
    }
}