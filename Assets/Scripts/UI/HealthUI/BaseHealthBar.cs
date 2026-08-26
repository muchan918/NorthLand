using System;
using NorthLand.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace NorthLand.UI
{
    public class BaseHealthBar : MonoBehaviour
    {
        [Serializable]
        private struct HealthSpriteStep
        {
            [Range(0f, 1f)]
            public float threshold;

            public Sprite sprite;
        }

        [SerializeField] private Slider slider;
        [SerializeField] private Image fillImage;

        [Tooltip("임계값이 높은 순서로 설정합니다. 마지막 항목은 0% fallback입니다.")]
        [SerializeField] private HealthSpriteStep[] healthSpriteSteps;

        private PlayerBase playerBase;
        private int appliedStepIndex = -1;

        void Awake()
        {
            gameObject.SetActive(false);

            if (slider == null)
            {
                Debug.LogWarning("[BaseHealthBar] Slider 참조가 없습니다.", this);
                return;
            }

            if (PlayerBase.Instance != null)
                Bind(PlayerBase.Instance);
            else
                PlayerBase.OnBaseSpawned += Bind;
        }

        void OnDestroy()
        {
            PlayerBase.OnBaseSpawned -= Bind;

            if (playerBase != null)
                playerBase.OnHpChanged -= UpdateBar;
        }

        void Bind(PlayerBase pb)
        {
            if (playerBase != null)
                playerBase.OnHpChanged -= UpdateBar;

            playerBase = pb;
            playerBase.OnHpChanged += UpdateBar;

            gameObject.SetActive(true);
            UpdateBar(playerBase.CurrentHp, playerBase.MaxHp);
        }

        void UpdateBar(float current, float max)
        {
            float ratio = max > 0f? Mathf.Clamp01(current / max): 0f;

            slider.value = ratio;
            UpdateHealthSprite(ratio);
        }

        private void UpdateHealthSprite(float ratio)
        {
            if (fillImage == null ||healthSpriteSteps == null ||healthSpriteSteps.Length == 0)
            {
                return;
            }

            int selectedIndex = healthSpriteSteps.Length - 1;

            for (int i = 0; i < healthSpriteSteps.Length - 1; i++)
            {
                if (ratio > healthSpriteSteps[i].threshold)
                {
                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex == appliedStepIndex)
                return;

            Sprite selectedSprite = healthSpriteSteps[selectedIndex].sprite;

            // 미할당 단계라면 현재 스프라이트를 유지한다.
            if (selectedSprite == null)
            {
                Debug.LogWarning(
                    $"[BaseHealthBar] {selectedIndex}번 체력 단계의 Sprite가 없습니다.",
                    this);

                return;
            }

            fillImage.sprite = selectedSprite;
            appliedStepIndex = selectedIndex;
        }
    }
}
