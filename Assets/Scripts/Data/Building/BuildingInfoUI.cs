using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 경영 공간 전용 건물 정보 패널. 선택된 생산 건물의 이름·레벨·업그레이드 정보를 표시하고,
// 업그레이드 버튼으로 ManagementController.TryUpgrade를 호출한다(로직/뷰 분리 — 계산·차감은 컨트롤러).
// TowerInfoUI(전투 공간)와 같은 계보의 별도 싱글톤.
// (주의) 이 오브젝트는 씬에서 '활성' 상태로 둬야 Awake가 실행되어 Instance가 등록된다. 인스펙터에서 미리 꺼두지 말 것.
public class BuildingInfoUI : MonoBehaviour
{
    public static BuildingInfoUI Instance { get; private set; }

    [Tooltip("업그레이드 데이터 소스. 비우면 씬에서 자동 탐색.")]
    [SerializeField] ManagementController _controller;

    [Header("BuildingInfoPanel 연결")]
    [Tooltip("건물명 (Lv 현재/최대)")]
    [SerializeField] TextMeshProUGUI _nameLevelText;
    [Tooltip("주민당 자원: 현재 → 업그레이드 후")]
    [SerializeField] TextMeshProUGUI _amountText;

    [Header("업그레이드 비용 (ScrollView)")]
    [Tooltip("비용 Row들이 생성될 ScrollView의 Content Transform")]
    [SerializeField] Transform _costContent;
    [Tooltip("비용 한 줄 프리팹 (BuildingCostRow)")]
    [SerializeField] BuildingCostRow _costRowPrefab;

    [SerializeField] Button _upgradeButton;

    private BuildingAsset _building;
    private int _lineIndex = -1;   // 현재 표시 중인 라인. -1 = 업그레이드 대상 아님(본진·스킬 건물 등)
    private bool _subscribed;
    private ResourceTable _resourceTable; // 비용 자원 Data 채움용(호출부 채움 규약, SystemMap §2)

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_upgradeButton != null)
        {
            _upgradeButton.onClick.AddListener(HandleUpgradeClicked);
        }
        HideInfo(); // Instance 등록 후 숨기므로 안전
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }
        Unsubscribe();
        if (_upgradeButton != null)
        {
            _upgradeButton.onClick.RemoveListener(HandleUpgradeClicked);
        }
        Instance = null;
    }

    // 건물 선택 시 호출(BuildingInfo.OnSelected). 컨트롤러에서 업그레이드 상태를 pull해 표시한다.
    public void ShowInfo(BuildingAsset building)
    {
        if (building == null || !EnsureController())
        {
            HideInfo();
            return;
        }

        _building = building;
        _lineIndex = _controller.LineIndexOf(building);
        Subscribe(); // 업그레이드·정산으로 상태가 바뀌면 자동 갱신
        gameObject.SetActive(true);
        Refresh();
    }

    public void HideInfo()
    {
        Unsubscribe();
        _building = null;
        _lineIndex = -1;
        gameObject.SetActive(false);
    }

    private void HandleUpgradeClicked()
    {
        // 성공 시 컨트롤러 OnChanged → Refresh가 자동으로 다시 그린다(실패해도 무해).
        if (_controller != null && _lineIndex >= 0)
        {
            _controller.TryUpgrade(_lineIndex);
        }
    }

    private void Refresh()
    {
        // 업그레이드 대상이 아닌 건물(본진·스킬 건물 등): 이름만 표시하고 업그레이드 행/버튼은 감춘다.
        if (_lineIndex < 0)
        {
            SetText(_nameLevelText, BuildingName());
            SetText(_amountText, string.Empty);
            ClearCostRows();
            if (_upgradeButton != null) _upgradeButton.gameObject.SetActive(false);
            return;
        }

        int level = _controller.LineLevel(_lineIndex);
        int max = _controller.LineMaxLevel(_lineIndex);
        int cur = _controller.LineAmountPerVillager(_lineIndex);
        bool isMax = level >= max;

        SetText(_nameLevelText, $"{BuildingName()} (Lv {level}/{max})");
        SetText(_amountText, isMax
            ? $"주민당 {cur} (MAX)"
            : $"주민당 {cur} → {_controller.LineNextAmountPerVillager(_lineIndex)}");
        if (isMax)
        {
            ClearCostRows();
        }
        else
        {
            RebuildCostRows(_controller.LineUpgradeCost(_lineIndex));
        }

        if (_upgradeButton != null)
        {
            _upgradeButton.gameObject.SetActive(true);
            _upgradeButton.interactable = _controller.CanUpgrade(_lineIndex);
        }
    }

    private string BuildingName()
    {
        if (_building == null || _building.Data == null)
        {
            return "-";
        }
        return LocalizationHelper.Get(LocalizationHelper.k_BuildingsTable, _building.Data.NameKey);
    }

    // 비용 자원마다 Row 프리팹을 하나씩 생성해 ScrollView Content에 채운다.
    // 각 Row는 지갑 보유량 대비 감당 여부(자원별)로 초록/회색이 갈린다.
    private void RebuildCostRows(IReadOnlyList<ResourceCost> costs)
    {
        ClearCostRows();
        if (costs == null || _costContent == null || _costRowPrefab == null)
        {
            return;
        }

        foreach (ResourceCost c in costs)
        {
            if (c == null || c.Resource == null || c.Amount <= 0)
            {
                continue;
            }
            ResourceData data = ResolveData(c.Resource);
            if (data == null)
            {
                continue;
            }

            string rname = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, data.NameKey);
            bool affordable = _controller.ResourceCount(data.Kind) >= c.Amount;

            BuildingCostRow row = Instantiate(_costRowPrefab, _costContent, false);
            row.Set(rname, c.Amount, affordable);
        }
    }

    // Content의 기존 Row(초기 예시 포함)를 모두 제거한다.
    private void ClearCostRows()
    {
        if (_costContent == null)
        {
            return;
        }
        for (int i = _costContent.childCount - 1; i >= 0; i--)
        {
            Destroy(_costContent.GetChild(i).gameObject);
        }
    }

    // ResourceAsset.Data 채움(호출부 채움 규약, SystemMap §2) — 비용 자원 종류·표시명 해석에 필요.
    private ResourceData ResolveData(ResourceAsset resource)
    {
        if (resource == null)
        {
            return null;
        }
        if (resource.Data == null)
        {
            _resourceTable ??= DataTableManager.Get<ResourceTable>("ResourceTable");
            if (_resourceTable != null)
            {
                resource.Data = _resourceTable.Get(resource.ResourceID);
            }
        }
        return resource.Data;
    }

    private static void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }

    private bool EnsureController()
    {
        if (_controller != null)
        {
            return true;
        }
        _controller = FindFirstObjectByType<ManagementController>();
        if (_controller == null)
        {
            Debug.LogWarning("[BuildingInfoUI] ManagementController를 찾지 못해 업그레이드 정보를 표시할 수 없습니다.", this);
            return false;
        }
        return true;
    }

    private void Subscribe()
    {
        if (_subscribed || _controller == null)
        {
            return;
        }
        _controller.OnChanged += Refresh;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || _controller == null)
        {
            return;
        }
        _controller.OnChanged -= Refresh;
        _subscribed = false;
    }
}
