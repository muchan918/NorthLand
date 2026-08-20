using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NorthLand.UI
{
    public sealed class NextWaveMonsterEntry : MonoBehaviour
    {
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

            if (countText != null)
            {
                countText.text = $"{count}";

                countText.color = isBoss? bossCountColor : normalCountColor;
            }
        }
    }
}
