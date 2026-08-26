using UnityEngine;
using UnityEngine.UI;

namespace NorthLand.UI
{
    public class RecipeMaterialIconView : MonoBehaviour
    {
        [SerializeField] private Image towerIcon;

        public void Initialize(Sprite icon)
        {
            towerIcon.sprite = icon;
            towerIcon.enabled = icon != null;
        }
    }
}