using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FusionTowerEntry : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image towerIcon;
    [SerializeField] private TMP_Text towerNameText;
    [SerializeField] private Button button;

    private TowerAsset tower;
    private Action<TowerAsset> onSelected;

    [Header("Rarity Colors")]
    [SerializeField]
    private Color normalColor = new Color(1f, 0.75f, 0.2f, 1f);

    [SerializeField]
    private Color rareColor = new Color(0.2f, 0.55f, 1f, 1f);

    [SerializeField]
    private Color legendaryColor = new Color(0.7f, 0.25f, 1f, 1f);

    [SerializeField] private Image buttonBackground;

    /// <summary>
    /// 도감 목록 항목을 초기화합니다.
    /// </summary>
    public void Initialize(TowerAsset targetTower,string displayName,Action<TowerAsset> selectedCallback)
    {
        tower = targetTower;
        onSelected = selectedCallback;

        UpdateView(displayName);
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
            buttonBackground.color = GetRarityColor(tower.Rarity);
        }
    }

    private Color GetRarityColor(TowerRarity rarity)
    {
        switch (rarity)
        {
            case TowerRarity.Rare:
                return rareColor;

            case TowerRarity.Legendary:
                return legendaryColor;

            case TowerRarity.Normal:
            default:
                return normalColor;
        }
    }

    private void RegisterButton()
    {
        if (button == null)
        {
            Debug.LogWarning($"[{nameof(FusionTowerEntry)}] Button이 연결되지 않았습니다.",this);

            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Select);
    }

    private void Select()
    {
        if (tower == null)
        {
            Debug.LogWarning($"[{nameof(FusionTowerEntry)}] TowerAsset이 없습니다.",this);

            return;
        }

        onSelected?.Invoke(tower);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(Select);
    }
}