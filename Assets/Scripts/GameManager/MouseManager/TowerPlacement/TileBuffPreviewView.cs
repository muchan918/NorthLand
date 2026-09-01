using TMPro;
using UnityEngine;

namespace NorthLand.UI
{
    /// <summary>
    /// 타워 배치 중 현재 풋프린트에 적용될 버프 타일 효과를 표시한다.
    /// 버프 계산은 담당하지 않고, TowerPlacer가 전달한 문구와 위치만 표시한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TileBuffPreviewView : MonoBehaviour
    {
        [Header("필수 배선")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TextMeshProUGUI buffText;

        [Header("표시 위치")]
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, 80f);

        private RectTransform rectTransform;
        private Canvas parentCanvas;
        private Camera worldCamera;
        private bool initialized;
        private bool initializationAttempted;
        private static bool s_wiringWarned;

        private void Awake()
        {
            Initialize();
        }

        public void Show(string text, Vector3 worldPosition)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Hide();
                return;
            }

            if (!Initialize())
            {
                return;
            }

            buffText.text = text;
            canvasGroup.alpha = 1f;

            gameObject.SetActive(true);

            UpdatePosition(worldPosition);
        }

        public void UpdatePosition(Vector3 worldPosition)
        {
            if (!gameObject.activeSelf || !Initialize())
            {
                return;
            }

            Camera camera = ResolveWorldCamera();

            if (camera == null)
            {
                Hide();
                return;
            }

            Vector3 screenPosition =
                camera.WorldToScreenPoint(worldPosition);

            if (screenPosition.z <= 0f)
            {
                Hide();
                return;
            }

            RectTransform canvasRect = parentCanvas != null ? parentCanvas.transform as RectTransform : null;

            if (canvasRect == null)
            {
                Hide();
                return;
            }

            Camera canvasCamera = parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? parentCanvas.worldCamera : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screenPosition,canvasCamera,out Vector2 localPosition))
            {
                Hide();
                return;
            }

            rectTransform.anchoredPosition = localPosition + screenOffset;
        }

        public void Hide()
        {
            if (buffText != null)
            {
                buffText.text = string.Empty;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            gameObject.SetActive(false);
        }



        private bool Initialize()
        {
            if (initializationAttempted)
            {
                return initialized;
            }

            initializationAttempted = true;

            rectTransform = transform as RectTransform;
            parentCanvas = GetComponentInParent<Canvas>(true);

            if (rectTransform == null ||
                parentCanvas == null ||
                canvasGroup == null ||
                buffText == null)
            {
                if (!s_wiringWarned)
                {
                    s_wiringWarned = true;

                    Debug.LogError(
                        "[TileBuffPreviewView] RectTransform, Canvas, " +
                        "CanvasGroup 또는 Buff Text가 준비되지 않았습니다.",
                        this);
                }

                enabled = false;
                return false;
            }

            initialized = true;
            return true;
        }

        private Camera ResolveWorldCamera()
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            return worldCamera;
        }
    }
}