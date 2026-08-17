using UnityEngine;
using UnityEngine.EventSystems;

public sealed class NextWavePreviewHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private GameObject previewPanel;

    private void Start()
    {
        if (previewPanel != null)
        {
            previewPanel.SetActive(false);
        }
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
        if (previewPanel != null)
        {
            previewPanel.SetActive(false);
        }
    }
}