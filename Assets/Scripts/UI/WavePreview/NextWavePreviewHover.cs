using UnityEngine;
using UnityEngine.EventSystems;

namespace NorthLand.UI
{
    public sealed class NextWavePreviewHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GameObject previewPanel;

        private void Start()
        {
            HidePreview();
        }

        private void OnDisable()
        {
            HidePreview();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (previewPanel != null)
            {
                previewPanel.SetActive(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HidePreview();
        }

        private void HidePreview()
        {
            if (previewPanel != null)
            {
                previewPanel.SetActive(false);
            }
        }
    }
}
