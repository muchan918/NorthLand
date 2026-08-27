using UnityEngine;
using UnityEngine.UI;

namespace NorthLand.UI
{
    public class RecipeMaterialIconView : MonoBehaviour
    {
        [SerializeField] private Image towerIcon;

        public void Initialize(Sprite icon)
        {
            if (towerIcon == null)
            {
                Debug.LogError("[RecipeMaterialIconView] Tower Icon이 연결되지 않았습니다.", this);

                return;
            }

            towerIcon.sprite = icon;
            towerIcon.enabled = icon != null;
        }
    }
}
