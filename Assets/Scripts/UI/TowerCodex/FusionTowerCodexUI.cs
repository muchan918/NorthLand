using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace NorthLand.UI
{
    public class FusionTowerCodexUI : MonoBehaviour
    {
        private const string TowerFolder = "ScriptableObjects/Towers";

        [Header("Panel")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;

        [Header("Tower List")]
        [SerializeField] private Transform content;
        [SerializeField] private FusionTowerEntry entryPrefab;

        [Header("Selected Tower")]
        [SerializeField] private Image selectedTowerIcon;
        [SerializeField] private TMP_Text selectedTowerNameText;
        [SerializeField] private TMP_Text selectedDescriptionText;
        [SerializeField] private TMP_Text selectedStatsText;


        [Header("Recipe")]
        [SerializeField] private GameObject recipeSection;
        [SerializeField] private Transform recipeIconContent;
        [SerializeField] private RecipeMaterialIconView recipeMaterialIconPrefab;
        [SerializeField] private TMP_Text recipeSeparatorPrefab;

        [Header("Details")]
        [SerializeField] private ScrollRect detailsScrollRect;

        private readonly Dictionary<TowerAsset, TowerRecipe> recipeByResult = new();

        private TowerAsset[] towers;
        private IReadOnlyList<TowerRecipe> recipes;
        private TowerAsset selectedTower;


        private void Awake()
        {
            RegisterButtons();
            LoadData();
            BuildRecipeLookup();
            BuildTowerEntries();
            SelectFirstTower();
        }

        private void RegisterButtons()
        {
            if (openButton != null)
                openButton.onClick.AddListener(Open);

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
        }

        private void LoadData()
        {
            towers = Resources.LoadAll<TowerAsset>(TowerFolder);
            recipes = TowerRecipeCatalog.All;

            Array.Sort(towers, (left, right) =>
            {
                if (left == null && right == null)
                    return 0;

                if (left == null)
                    return 1;

                if (right == null)
                    return -1;

                int rarityCompare = left.Rarity.CompareTo(right.Rarity);

                if (rarityCompare != 0)
                    return rarityCompare;

                return string.CompareOrdinal(TowerDisplayName.Of(left), TowerDisplayName.Of(right));
            });
        }

        private void BuildRecipeLookup()
        {
            recipeByResult.Clear();

            foreach (TowerRecipe recipe in recipes)
            {
                if (recipe == null || recipe.Result == null)
                    continue;

                if (recipeByResult.ContainsKey(recipe.Result))
                {
                    Debug.LogWarning($"[FusionTowerCodexUI] '{recipe.Result.TowerID}'의 레시피가 두 개 이상 존재합니다.", recipe);

                    continue;
                }

                recipeByResult.Add(recipe.Result, recipe);
            }
        }

        private void BuildTowerEntries()
        {
            if (content == null || entryPrefab == null)
            {
                Debug.LogError("[FusionTowerCodexUI] Content 또는 Entry Prefab이 연결되지 않았습니다.", this);

                return;
            }

            ClearEntries();

            foreach (TowerAsset tower in towers)
            {
                if (tower == null)
                    continue;

                FusionTowerEntry entry = Instantiate(entryPrefab, content);

                entry.Initialize(tower, TowerDisplayName.Of(tower), SelectTower);
            }
        }

        private void ClearEntries()
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Destroy(content.GetChild(i).gameObject);
            }
        }

        private void SelectFirstTower()
        {
            if (towers == null || towers.Length == 0)
            {
                ClearSelectedView();
                return;
            }

            foreach (TowerAsset tower in towers)
            {
                if (tower == null)
                    continue;

                SelectTower(tower);
                return;
            }

            ClearSelectedView();
        }

        private void SelectTower(TowerAsset tower)
        {
            selectedTower = tower;

            if (tower == null)
            {
                ClearSelectedView();
                return;
            }

            if (selectedTowerIcon != null)
            {
                selectedTowerIcon.sprite = tower.Icon;
                selectedTowerIcon.enabled = tower.Icon != null;
            }

            if (selectedTowerNameText != null)
                selectedTowerNameText.text = TowerInfoFormatter.BuildHeader(tower);

            string description = TowerInfoFormatter.BuildDescription(tower);

            if (selectedDescriptionText != null)
            {
                selectedDescriptionText.text = description;
                selectedDescriptionText.gameObject.SetActive(!string.IsNullOrEmpty(description));
            }

            if (selectedStatsText != null)
                selectedStatsText.text = TowerInfoFormatter.BuildStats(tower);

            recipeByResult.TryGetValue(tower, out TowerRecipe recipe);

            BuildRecipeIcons(recipe);
            ResetDetailsScroll();

        }

        private void ResetDetailsScroll()
        {
            if (detailsScrollRect == null)
                return;

            detailsScrollRect.StopMovement();
            Canvas.ForceUpdateCanvases();
            detailsScrollRect.verticalNormalizedPosition = 1f;
        }

        private void ClearSelectedView()
        {
            if (selectedDescriptionText != null)
            {
                selectedDescriptionText.text = string.Empty;
                selectedDescriptionText.gameObject.SetActive(false);
            }

            if (recipeSection != null)
                recipeSection.SetActive(false);

            if (selectedStatsText != null)
                selectedStatsText.text = string.Empty;

            if (selectedTowerIcon != null)
            {
                selectedTowerIcon.sprite = null;
                selectedTowerIcon.enabled = false;
            }


            if (selectedTowerNameText != null)
                LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, "codex.tower.none");


            ClearRecipeIcons();
        }

        public void Open()
        {
            MouseManager.Instance?.CancelInteractions();

            if (panelRoot != null)
                panelRoot.SetActive(true);
        }

        public void Close()
        {
            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (openButton != null)
                openButton.onClick.RemoveListener(Open);

            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
        }
        private void OnEnable()
        {
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        }
        private void OnDisable()
        {
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged(Locale locale)
        {
            BuildTowerEntries();

            if (selectedTower != null)
                SelectTower(selectedTower);
            else
                SelectFirstTower();
        }


        private void BuildRecipeIcons(TowerRecipe recipe)
        {
            ClearRecipeIcons();

            bool hasRecipe = recipe != null && recipe.Materials != null && recipe.Materials.Count > 0;

            if (recipeSection != null)
                recipeSection.SetActive(hasRecipe);

            if (!hasRecipe ||recipeIconContent == null ||recipeMaterialIconPrefab == null)
            {
                return;
            }

            bool isFirstIcon = true;

            foreach (TowerRecipe.MaterialEntry material in recipe.Materials)
            {
                if (material == null ||material.Tower == null ||material.Count <= 0)
                {
                    continue;
                }

                for (int i = 0; i < material.Count; i++)
                {
                    if (!isFirstIcon && recipeSeparatorPrefab != null)
                        Instantiate(recipeSeparatorPrefab, recipeIconContent);

                    RecipeMaterialIconView icon = Instantiate(recipeMaterialIconPrefab,recipeIconContent);

                    icon.Initialize(material.Tower.Icon);
                    isFirstIcon = false;
                }
            }
        }

        private void ClearRecipeIcons()
        {
            if (recipeIconContent == null)
                return;

            for (int i = recipeIconContent.childCount - 1; i >= 0; i--)
                Destroy(recipeIconContent.GetChild(i).gameObject);
        }
    }
}
