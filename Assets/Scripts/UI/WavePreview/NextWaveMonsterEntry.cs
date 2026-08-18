using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NorthLand.UI
{
    public sealed class NextWaveMonsterEntry : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text countText;

        public void Bind(Sprite icon, int count)
        {
            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }

            if (countText != null)
            {
                countText.text = $"* {count}";
            }
        }
    }
}
