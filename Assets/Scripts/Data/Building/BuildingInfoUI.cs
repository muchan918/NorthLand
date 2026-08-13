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
    // 마법 연구소 등 업그레이드 전용 건물의 효과 줄 라벨. 생산 건물의 "주민당 5→7" 자리에 표시된다.
    // 구체적인 강화 수치는 업그레이드 버튼 위 별도 줄(k_LabEffectKey)이 담당하고, 이 자리는
    // 그 줄을 만들 수 없을 때(문구 키 미등록·SO에 레벨 없음)의 폴백으로 남는다.
    private const string k_SkillPendingKey = "building.upgrade.skill_pending";
    // 스킬 강화 건물의 "이번 업그레이드로 얼마나 오르는지" 문구(#375). 레벨마다 문장이 아니라 숫자만
    // 달라지므로 본진(castle.effect.lv{n})과 달리 레벨별로 키를 쪼개지 않는다 — 레벨이 늘어도 문구 작업이 없다.
    //
    // 대신 **스탯별로** 쪼갠다: 한 키에 3줄을 몰아넣으면 배율이 그대로인 스탯까지 "0% 증가합니다"로
    // 나온다(PR#378 리뷰). 줄 단위로 나눠야 변한 스탯만 골라 이어 붙일 수 있다.
    private const string k_LabDamageKey = "lab.effect.damage";
    private const string k_LabRadiusKey = "lab.effect.radius";
    private const string k_LabCooldownKey = "lab.effect.cooldown";
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
    [Tooltip("업그레이드 버튼 위 강화 효과 문구 (마법 연구소 등 스킬 강화 건물에서만 채워진다)")]
    [SerializeField] TextMeshProUGUI _skillEffectText;

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
        // 강화 문구는 스킬 강화 건물만 채운다. 같은 패널을 모든 건물이 재사용하므로 여기서 먼저 비워야
        // 마법 연구소를 보다 생산 건물을 클릭했을 때 이전 문구가 남지 않는다(분기 추가 시 빠뜨릴 자리를 없앤다).
        SetText(_skillEffectText, string.Empty);

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
    // 생산 라인과 달리 "주민당 자원" 같은 즉시 효과값이 없어, 대신 업그레이드 버튼 위에 이번 업그레이드로
    // 스킬 스탯이 몇 % 변하는지를 문장으로 보여준다(#375).
    private void RefreshUpgradeBuilding()
    {
        int level = _controller.UpgradeBuildingLevel(_upgradeIndex);
        int max = _controller.UpgradeBuildingMaxLevel(_upgradeIndex);
        bool isMax = level >= max;

        SetText(_nameLevelText, $"{BuildingName()} ({L(k_LevelKey)} {level}/{max})");
        // 본진 레벨로 잠긴 상태면 강화 안내 대신 잠금 사유를 보여준다 — 지금 필요한 정보는 그쪽이다(#229).
        string lockNotice = LockNotice(_controller.UpgradeBuildingRequiredCastleLevel(_upgradeIndex));
        SetText(_amountText, lockNotice ?? L(k_SkillPendingKey));
        // 최대 도달이면 안내할 다음 효과가 없고, 잠긴 상태면 지금 필요한 정보는 잠금 사유 쪽이다 —
        // 둘 다 빈 줄로 두면 CSF가 그 자리를 접는다(CastlePanelUI의 _upgradeEffectText와 같은 규약).
        SetText(_skillEffectText, (isMax || lockNotice != null) ? string.Empty : UpgradeEffect(level + 1));
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

    // 이번 업그레이드로 스킬 스탯이 몇 % 변하는지(#375). 배율 테이블(SO)에서 직접 산출하므로
    // 밸런싱이 magic_lab.asset을 고치면 문구가 자동으로 따라가고, SkillManager가 실제 적용하는 값과
    // 어긋날 수 없다 — 표시용 수치를 따로 authoring하지 않는 이유다(SkillEffect 계보의 규약).
    // 그 대가로 이 UI는 스킬 시스템을 참조하지 않는다(베이스 스탯은 SkillManager 소유, 여기선 배율만 안다).
    //
    // 인자는 '지금 누르면 도달할' 레벨(1-based)이다. 문구 키가 없으면 LocalizationHelper.Get이
    // 에러를 내지만, 키는 레벨과 무관한 고정 3개뿐이라 본진(castle.effect.lv{n})처럼 엔트리 존재를
    // 확인할 필요가 없다 — 없으면 그건 세팅 실수고 드러나는 편이 낫다.
    private string UpgradeEffect(int nextLevel)
    {
        BuildingAsset.SkillUpgradeLevel next = Scaling(nextLevel);
        if (next == null)
        {
            return string.Empty; // 스킬 강화 건물이 아니거나 authoring된 레벨이 없다 — _amountText 폴백이 받는다.
        }
        // 강화되는 스탯만 줄로 만든다. 세 줄을 무조건 채우면 그 레벨에서 안 건드리는 스탯(배율 1.0)이
        // "0% 증가합니다"로 나온다 — 수치가 아직 placeholder(TBD)라 밸런싱 패스에서 한 레벨에 한 스탯만
        // 올리는 순간 바로 나타난다(PR#378 리뷰).
        var lines = new List<string>(3);
        AddEffectLine(lines, k_LabDamageKey, next.DamageMultiplier, false);
        AddEffectLine(lines, k_LabRadiusKey, next.RadiusMultiplier, false);
        AddEffectLine(lines, k_LabCooldownKey, next.CooldownMultiplier, true);
        return string.Join("\n", lines);
    }

    // 증감률이 양수일 때만 줄을 추가한다. 0은 "변화 없음"이라 할 말이 없고, 음수는 업그레이드가 스탯을
    // 깎았다는 뜻이라 authoring 실수다 — 어느 쪽도 "N% 증가합니다" 문장에 넣으면 거짓이 되므로 줄을 비운다
    // (RewardCardView가 수치 없는 보상 카드를 통째로 비우는 것과 같은 규약).
    private static void AddEffectLine(List<string> lines, string key, float multiplier, bool inverted)
    {
        int pct = Pct(multiplier, inverted);
        if (pct > 0)
        {
            lines.Add(LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, key, pct));
        }
    }

    // 레벨 n(1-based)의 배율 엔트리. 0이나 범위 밖이면 null = 강화 없음(SkillManager.RefreshUpgrade와 같은 규약).
    //
    // 컨트롤러가 레벨·MAX를 세는 것과 **물리적으로 같은 리스트**를 읽어야 한다(PR#378 리뷰). `UpgradeSteps`는
    // Castle→Skill→Production→Exchange 중 첫 비어있지 않은 그룹 하나만 돌려주므로(BuildingAsset.cs:31),
    // 여기서 `Skill`을 고정으로 읽으면 두 그룹을 동시에 authoring한 건물에서 "Lv 2/3인데 4행째의 %"가 뜬다.
    // 고른 그룹이 스킬 강화가 아니면 `as`가 null → 문구 없음(폴백)이라 타입 판정도 겸한다.
    private BuildingAsset.SkillUpgradeLevel Scaling(int level)
    {
        IReadOnlyList<BuildingAsset.UpgradeStep> steps = _building?.UpgradeSteps;
        return (steps == null || level < 1 || level > steps.Count)
            ? null
            : steps[level - 1] as BuildingAsset.SkillUpgradeLevel;
    }

    // 도달 레벨의 배율을 **기본 스탯 대비 증감률**로 환산한다. 배율 1.4 → "40% 증가".
    //
    // 직전 레벨과 비교하지 않는 이유: 배율은 누적 곱이 아니라 기본값에 **한 번만** 곱해진다
    // (`SkillManager.cs:150-152`의 `damage * scaling.DamageMultiplier`). 즉 배율 자체가 이미 기본 대비
    // 총량이라, 레벨을 올려도 "지금 상태가 기본의 몇 배인가"가 그대로 읽힌다. 직전 레벨 대비 델타로
    // 바꾸면 선형 authoring(1.2/1.4/1.6/1.8/2.0)이 20/17/14/12/11로 보여 같은 폭인데 나빠지는 것처럼 읽힌다.
    //
    // inverted는 쿨다운처럼 '낮을수록 이득'인 스탯용 — 배율 0.8을 감소폭 20%로 뒤집는다.
    // 0/음수 배율을 1.0으로 막는 건 SkillManager.PositiveOr1과 같은 방어다(authoring 실수 흡수).
    private static int Pct(float multiplier, bool inverted)
    {
        float m = multiplier > 0f ? multiplier : 1f;
        return Mathf.RoundToInt((inverted ? 1f - m : m - 1f) * 100f);
    }

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
