using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

// 경영 공간 전용 건물 정보 패널. 선택된 생산 건물의 이름·레벨·업그레이드 정보를 표시하고,
// 업그레이드 버튼으로 ManagementController.TryUpgrade를 호출한다(로직/뷰 분리 — 계산·차감은 컨트롤러).
// TowerInfoUI(전투 공간)와 같은 계보의 별도 싱글톤.
// (주의) 이 오브젝트는 씬에서 '활성' 상태로 둬야 Awake가 실행되어 Instance가 등록된다. 인스펙터에서 미리 꺼두지 말 것.
public class BuildingInfoUI : MonoBehaviour
{
    public static BuildingInfoUI Instance { get; private set; }

    // NorthLand_default 스트링 테이블 키 (짧은 라벨을 넣고 코드에서 조합 — ManagementPanelView 계보).
    private const string k_PerVillagerKey = "building.upgrade.per_villager";
    private const string k_MaxKey = "building.upgrade.max";
    private const string k_LevelKey = "building.upgrade.level";
    // 마법 연구소 등 업그레이드 전용 건물의 효과 줄 라벨. 생산 건물의 "주민당 5→7" 자리에 표시된다 —
    // 강화 효과(스킬 강화)는 이번 범위 밖(추후 구현)이라 수치 대신 이 안내 문구를 보여준다.
    private const string k_SkillPendingKey = "building.upgrade.skill_pending";
    // 본진 레벨 부족으로 다음 레벨이 잠긴 상태(#229). 진짜 최대(k_MaxKey)와 구분해야
    // "왜 못 올리는지"가 보인다 — Smart String {0}에 필요한 본진 레벨이 들어간다.
    private const string k_RequireCastleKey = "building.upgrade.require_castle";

    [Tooltip("업그레이드 데이터 소스. 비우면 씬에서 자동 탐색.")]
    [SerializeField] ManagementController _controller;

    [Header("BuildingInfoPanel 연결")]
    [Tooltip("건물명 (Lv 현재/최대)")]
    [SerializeField] TextMeshProUGUI _nameLevelText;
    [Tooltip("업그레이드 시 증감: 현재 → 업그레이드 후")]
    [SerializeField] TextMeshProUGUI _amountText;
    [Tooltip("건물 설명 (BuildingTable.DescriptionKey)")]
    [SerializeField] TextMeshProUGUI _descriptionText;

    [Header("주민당 생산 (ProduceRow — 생산 건물에서만 표시)")]
    [Tooltip("생산 줄 전체. 생산 건물이 아니면 통째로 숨긴다.")]
    [SerializeField] GameObject _produceRow;
    [Tooltip("산출 자원 아이콘 (ResourceAsset.Icon)")]
    [SerializeField] Image _produceIcon;
    [Tooltip("주민당 현재 생산량")]
    [SerializeField] TextMeshProUGUI _produceAmountText;

    [Header("업그레이드 비용 (ScrollView)")]
    [Tooltip("비용 Row들이 생성될 ScrollView의 Content Transform")]
    [SerializeField] Transform _costContent;
    [Tooltip("비용 한 줄 프리팹 (BuildingCostRow)")]
    [SerializeField] BuildingCostRow _costRowPrefab;

    [SerializeField] Button _upgradeButton;

    private BuildingAsset _building;
    private int _lineIndex = -1;      // 생산 라인(주민당량 업그레이드) index. -1 = 생산 라인 아님
    private int _upgradeIndex = -1;   // 업그레이드 전용 건물(마법 연구소 등) index. -1 = 업그레이드 건물 아님
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
        LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;
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
        LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;
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
        _lineIndex = _controller.LineIndexOf(building);           // 생산 라인(나무·철·식량)
        _upgradeIndex = _controller.UpgradeIndexOf(building);     // 업그레이드 전용 건물(마법 연구소 등)
        Subscribe(); // 업그레이드·정산으로 상태가 바뀌면 자동 갱신
        gameObject.SetActive(true);
        Refresh();
    }

    public void HideInfo()
    {
        Unsubscribe();
        _building = null;
        _lineIndex = -1;
        _upgradeIndex = -1;
        gameObject.SetActive(false);
    }

    private void HandleUpgradeClicked()
    {
        // 성공 시 컨트롤러 OnChanged → Refresh가 자동으로 다시 그린다(실패해도 무해).
        if (_controller == null)
        {
            return;
        }
        if (_lineIndex >= 0)
        {
            _controller.TryUpgrade(_lineIndex);            // 생산 라인: 주민당량 업그레이드
        }
        else if (_upgradeIndex >= 0)
        {
            _controller.TryUpgradeBuilding(_upgradeIndex); // 마법 연구소 등: 레벨 업그레이드(마나석)
        }
    }

    private void Refresh()
    {
        // 설명은 건물 종류와 무관하게 같은 소스라 분기 이전에 한 번만 채운다.
        SetText(_descriptionText, BuildingDescription());

        // 표시 대상 분기: 생산 라인(주민당량) → 업그레이드 전용 건물(마법 연구소 등) → 그 외(본진 등, 이름만).
        if (_lineIndex >= 0)
        {
            RefreshProductionLine();
        }
        else if (_upgradeIndex >= 0)
        {
            RefreshUpgradeBuilding();
        }
        else
        {
            RefreshNonUpgradeable();
        }
    }

    // 업그레이드 대상이 아닌 건물(본진 등): 이름만 표시하고 업그레이드 행/버튼은 감춘다.
    private void RefreshNonUpgradeable()
    {
        SetText(_nameLevelText, BuildingName());
        SetText(_amountText, string.Empty);
        HideProduceRow();
        ClearCostRows();
        if (_upgradeButton != null) _upgradeButton.gameObject.SetActive(false);
    }

    // 마법 연구소 등 업그레이드 전용 건물: 이름(Lv 현재/최대) + 효과 안내 + 비용(마나석) + 업그레이드 버튼.
    // 생산 라인과 달리 "주민당 자원" 같은 즉시 효과값이 없다 — 강화 효과(스킬 강화)는 스킬 시스템이 레벨을 참조해
    // 정하는 후속(TODO)이라, 효과 줄엔 수치를 지어내지 않고 "이번 범위 밖(추후 구현)" 안내 문구를 표시한다.
    private void RefreshUpgradeBuilding()
    {
        int level = _controller.UpgradeBuildingLevel(_upgradeIndex);
        int max = _controller.UpgradeBuildingMaxLevel(_upgradeIndex);
        bool isMax = level >= max;

        SetText(_nameLevelText, $"{BuildingName()} ({L(k_LevelKey)} {level}/{max})");
        // 본진 레벨로 잠긴 상태면 강화 안내 대신 잠금 사유를 보여준다 — 지금 필요한 정보는 그쪽이다(#229).
        string lockNotice = LockNotice(_controller.UpgradeBuildingRequiredCastleLevel(_upgradeIndex));
        SetText(_amountText, lockNotice ?? L(k_SkillPendingKey));
        HideProduceRow(); // 주민당 산출이 없는 건물
        if (isMax)
        {
            ClearCostRows();
        }
        else
        {
            RebuildCostRows(_controller.UpgradeBuildingCost(_upgradeIndex));
        }

        if (_upgradeButton != null)
        {
            _upgradeButton.gameObject.SetActive(true);
            _upgradeButton.interactable = _controller.CanUpgradeBuilding(_upgradeIndex);
        }
    }

    private void RefreshProductionLine()
    {
        int level = _controller.LineLevel(_lineIndex);
        int max = _controller.LineMaxLevel(_lineIndex);
        int cur = _controller.LineAmountPerVillager(_lineIndex);
        bool isMax = level >= max;

        SetText(_nameLevelText, $"{BuildingName()} ({L(k_LevelKey)} {level}/{max})");
        // 현재 산출은 ProduceRow(아이콘 + 주민당 현재량), 업그레이드 증감은 _amountText로 나눠 표시한다.
        // 자원 종류는 아이콘이 말하므로 여기 텍스트에는 자원명을 넣지 않는다.
        ShowProduceRow(_building != null && _building.Production != null ? _building.Production.OutputResource : null,
            $"{L(k_PerVillagerKey)} {cur}");
        // 잠긴 경우 "MAX"가 아니라 "본진 Lv n 필요"가 된다 — 더 올릴 수 있다는 사실이 드러나야 한다(#229).
        string lockNotice = LockNotice(_controller.LineRequiredCastleLevel(_lineIndex));
        SetText(_amountText, isMax
            ? (lockNotice ?? L(k_MaxKey))
            : $"{cur} → {_controller.LineNextAmountPerVillager(_lineIndex)}");
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

    // 다음 레벨이 본진 레벨 부족으로 잠겼으면 "본진 Lv n 필요", 아니면 null(호출부가 기존 MAX 표기로 폴백).
    // 진짜 최대(더 이상 행이 없음)와 잠김(본진을 올리면 열림)은 플레이어에게 전혀 다른 정보다.
    //
    // ⚠ 인자는 컨트롤러가 주는 '내부' 레벨(0 = 미업그레이드)이고, 화면에 쓰는 본진 레벨은 +1한 값이다
    //   (CastlePanelUI가 GetUpgradeLevel + 1로 제목을 그리는 것과 같은 규약). 여기서 변환하지 않으면
    //   본진 Lv2인데 "본진 Lv2 필요"처럼 이미 만족한 조건을 요구하는 것으로 보인다.
    private static string LockNotice(int requiredCastleLevel) =>
        requiredCastleLevel > 0
            ? LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, k_RequireCastleKey, requiredCastleLevel + 1)
            : null;

    private string BuildingName()
    {
        if (_building == null || _building.Data == null)
        {
            return "-";
        }
        return LocalizationHelper.Get(LocalizationHelper.k_BuildingsTable, _building.Data.NameKey);
    }

    private string BuildingDescription()
    {
        if (_building == null || _building.Data == null)
        {
            return string.Empty;
        }
        return LocalizationHelper.Get(LocalizationHelper.k_BuildingsTable, _building.Data.DescriptionKey);
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

            // 자원 종류는 아이콘으로만 표기한다 — Data는 지갑 대조(Kind)에만 쓰인다.
            bool affordable = _controller.ResourceCount(data.Kind) >= c.Amount;

            BuildingCostRow row = Instantiate(_costRowPrefab, _costContent, false);
            row.Set(c.Resource.Icon, c.Amount, affordable);
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

    // 생산 줄을 켜고 아이콘·수량을 채운다. 아이콘 미할당 SO는 흰 사각형 대신 빈 칸으로 둔다(BuildingCostRow와 같은 규약).
    private void ShowProduceRow(ResourceAsset output, string amountLabel)
    {
        if (_produceRow != null)
        {
            _produceRow.SetActive(true);
        }
        if (_produceIcon != null)
        {
            Sprite icon = output != null ? output.Icon : null;
            _produceIcon.enabled = icon != null;
            _produceIcon.sprite = icon;
        }
        SetText(_produceAmountText, amountLabel);
    }

    private void HideProduceRow()
    {
        if (_produceRow != null)
        {
            _produceRow.SetActive(false);
        }
    }

    private static void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }

    // NorthLand_default 키를 현재 로케일로 조회(짧은 라벨). 지속 표시지만 OnChanged마다 재조회돼 갱신된다(ManagementPanelView와 동일 pull 방식).
    private static string L(string key) => LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, key);

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

    private void HandleLocaleChanged(UnityEngine.Localization.Locale locale)
    {
        if (_building != null)
        {
            Refresh();
        }
    }
}
