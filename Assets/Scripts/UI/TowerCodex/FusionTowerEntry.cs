using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Threading;
using Cysharp.Threading.Tasks;


namespace NorthLand.UI
{
    public class FusionTowerEntry : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Image towerIcon;
        [SerializeField] private TMP_Text towerNameText;
        [SerializeField] private Button button;

        private TowerAsset tower;
        private Action<TowerAsset> onSelected;

        [Header("Rarity Images")]
        [SerializeField] private Sprite normalSprite;
        [SerializeField] private Sprite rareSprite;
        [SerializeField] private Sprite legendarySprite;

        [SerializeField] private Image buttonBackground;

        private CancellationTokenSource scaleCancellation;
        [SerializeField]
        private float selectedScale = 1.06f;

        [SerializeField]
        private Color unselectedColor = new Color(0.85f, 0.85f, 0.85f, 1f);

        [SerializeField]
        private float animationDuration = 0.12f;

        private Vector3 originalScale;

        /// <summary>
        /// 도감 목록 항목을 초기화합니다.
        /// </summary>
        public void Initialize(TowerAsset targetTower,string displayName,Action<TowerAsset> selectedCallback)
        {
            tower = targetTower;
            onSelected = selectedCallback;
            originalScale = transform.localScale;

            UpdateView(displayName);
            SetSelected(false);
            RegisterButton();
        }
        private void UpdateView(string displayName)
        {
            if (tower == null)
            {
                if (towerIcon != null)
                {
                    towerIcon.sprite = null;
                    towerIcon.enabled = false;
                }

                if (towerNameText != null)
                    towerNameText.text = "정보 없음";

                return;
            }

            if (towerIcon != null)
            {
                towerIcon.sprite = tower.Icon;
                towerIcon.enabled = tower.Icon != null;
            }

            if (towerNameText != null)
            {
                towerNameText.text = string.IsNullOrWhiteSpace(displayName) ? tower.TowerID : displayName;
            }

            if (buttonBackground != null)
            {
                Sprite raritySprite = GetRaritySprite(tower.Rarity);

                buttonBackground.sprite = raritySprite;
                buttonBackground.enabled = raritySprite != null;
                buttonBackground.color = Color.white;
            }
        }

        private Sprite GetRaritySprite(TowerRarity rarity)
        {
            switch (rarity)
            {
                case TowerRarity.Rare:
                    return rareSprite;

                case TowerRarity.Legendary:
                    return legendarySprite;

                case TowerRarity.Normal:
                default:
                    return normalSprite;
            }
        }

        private void RegisterButton()
        {
            if (button == null)
            {
                Debug.LogWarning($"[{nameof(FusionTowerEntry)}] Button이 연결되지 않았습니다.", this);

                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(Select);
        }

        internal void SetSelected(bool selected)
        {
            if (buttonBackground != null)
                buttonBackground.color = selected ? Color.white : unselectedColor;

            scaleCancellation?.Cancel();
            scaleCancellation?.Dispose();
            scaleCancellation = new CancellationTokenSource();

            Vector3 targetScale = selected ? originalScale * selectedScale : originalScale;

            AnimateScaleAsync(targetScale, scaleCancellation.Token).Forget();
        }

        private async UniTask AnimateScaleAsync(Vector3 targetScale,CancellationToken cancellationToken)
        {
            Vector3 startScale = transform.localScale;
            float elapsed = 0f;

            try
            {
                while (elapsed < animationDuration)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / animationDuration);
                    t = 1f - Mathf.Pow(1f - t, 3f);

                    transform.localScale = Vector3.LerpUnclamped(startScale, targetScale, t);

                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }

                transform.localScale = targetScale;
            }
            catch (OperationCanceledException)
            {
                // 다른 선택 애니메이션이 시작되면 이전 작업 종료
            }
        }
        public void Select()
        {
            if (tower == null)
                return;

            onSelected?.Invoke(tower);
        }
        private void OnDestroy()
        {
            if (button != null)
                button.onClick.RemoveListener(Select);

            scaleCancellation?.Cancel();
            scaleCancellation?.Dispose();

        }

    }
}