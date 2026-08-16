using System;
using System.Collections.Generic;
using System.Text;
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
        [SerializeField] private TMP_Text selectedRecipeText;

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

                return string.CompareOrdinal(GetTowerName(left), GetTowerName(right));
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

                entry.Initialize(tower, GetTowerName(tower), SelectTower);
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
                selectedTowerNameText.text = GetTowerName(tower);

            if (selectedRecipeText == null)
                return;

            if (recipeByResult.TryGetValue(tower, out TowerRecipe recipe))
            {
                selectedRecipeText.text = BuildRecipeText(recipe);
            }
            else
            {
                selectedRecipeText.text = LocalizationHelper.Get(LocalizationHelper.k_TowersTable, "towers.normal");
            }
        }

        private string BuildRecipeText(TowerRecipe recipe)
        {
            if (recipe == null || recipe.Materials == null || recipe.Materials.Count == 0)
            {
                return "조합 정보 없음";
            }

            Dictionary<TowerAsset, int> materialCounts = new();

            foreach (TowerRecipe.MaterialEntry material in recipe.Materials)
            {
                if (material == null || material.Tower == null || material.Count <= 0)
                {
                    continue;
                }

                if (materialCounts.ContainsKey(material.Tower))
                    materialCounts[material.Tower] += material.Count;
                else
                    materialCounts.Add(material.Tower, material.Count);
            }

            if (materialCounts.Count == 0)
                return "조합 정보 없음";

            StringBuilder builder = new();
            bool first = true;

            foreach (KeyValuePair<TowerAsset, int> material in materialCounts)
            {
                if (!first)
                {
                    builder.Append("\n+ \n");
                }
                builder.Append(GetTowerName(material.Key));

                if (material.Value > 1)
                {
                    builder.Append(" × ");
                    builder.Append(material.Value);

                }

                first = false;
            }
            return builder.ToString();
        }

        private static string GetTowerName(TowerAsset tower)
        {
            if (tower == null)
                return "정보 없음";

            if (string.IsNullOrWhiteSpace(tower.TowerID))
                return tower.name;

            TowerTable towerTable = DataTableManager.Get<TowerTable>("TowerTable");

            if (towerTable == null)
                return tower.TowerID;

            TowerData data = towerTable.Get(tower.TowerID);

            if (data == null || string.IsNullOrWhiteSpace(data.NameKey))
            {
                return tower.TowerID;
            }

            string localizedName = LocalizationHelper.Get(LocalizationHelper.k_TowersTable, data.NameKey);

            return string.IsNullOrWhiteSpace(localizedName) ? tower.TowerID : localizedName;
        }

        private void ClearSelectedView()
        {
            if (selectedTowerIcon != null)
            {
                selectedTowerIcon.sprite = null;
                selectedTowerIcon.enabled = false;
            }

            if (selectedTowerNameText != null)
                selectedTowerNameText.text = "타워 없음";

            if (selectedRecipeText != null)
                selectedRecipeText.text = "조합 정보 없음";
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
    }
}
