using UnityEngine;

namespace NorthLand.UI
{
    public static class UiPalette
    {
        public static readonly Color Positive = new Color32(0x72, 0xB4, 0x55, 0xFF);

        public static readonly string PositiveHex = $"#{ColorUtility.ToHtmlStringRGB(Positive)}";
    }
}