using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 본진 전용 패널(#227, #229). 서로 독립적인 두 축을 나란히 보여준다 —
//  ① 주민 수 증가: 자원을 소모해 총 보유 주민을 1 늘린다(TryIncreaseVillagers)
//  ② 본진 업그레이드: 자원을 소모해 본진 레벨을 1 올린다(TryUpgradeBuilding) → 하위 건물 Max 해금 + 연금술사 교환 효율 상승
// 각 축은 자기 비용 목록과 버튼을 짝지어 갖는다. 로직·비용 차감은 전부 ManagementController에 위임한다(뷰는 로직 없음).
//
// BuildingInfoUI(정보 표시) / StorePanelUI(교환 목록)와 같은 계보의 별도 씬 싱글톤이다.
// 셋은 서로 배타적이며, 어느 것을 열지는 BuildingInfo가 '데이터 존재'로 분기한다(BuildingType이 아니다).
// (주의) 이 오브젝트는 씬에서 '활성' 상태로 둬야 Awake가 실행되어 Instance가 등록된다. 인스펙터에서 미리 꺼두지 말 것.
public class CastlePanelUI : MonoBehaviour
{
    public static CastlePanelUI Instance { get; private set; }

    // NorthLand_default 스트링 테이블 키 (짧은 라벨을 넣고 코드에서 조합 — BuildingInfoUI 계보).
    private const string k_MaxVillagersKey = "castle.max_villagers";
    private const string k_AddVillagerKey = "castle.add_villager";
    // 레벨/최대 표기는 건물 업그레이드 패널과 같은 라벨을 쓴다(중복 키를 만들지 않는다).
    private const string k_LevelKey = "building.upgrade.level";
    private const string k_MaxKey = "building.upgrade.max";
    private const string k_UpgradeKey = "game.btn.upgrade";
    // 비용 박스 헤더 — 목록이 둘이라 어느 쪽 비용인지 표시가 필요하다. 기존 키를 재사용한다(중복 키 금지).
    private const string k_CostKey = "building.upgrade.cost";

    [Tooltip("주민·자원 상태 소스. 비우면 씬에서 자동 탐색.")]
    [SerializeField] ManagementController _controller;

    [Header("CastlePanel 연결")]
    [Tooltip("패널 제목 (건물명 Lv n)")]
    [SerializeField] TextMeshProUGUI _titleText;
    [Tooltip("주민 현황 (배치/총 보유)")]
    [SerializeField] TextMeshProUGUI _villagerText;

    [Header("주민 증가 비용 (ScrollView)")]
    [Tooltip("주민 증가 비용 Row들이 생성될 ScrollView의 Content Transform")]
    [SerializeField] Transform _costContent;
    [Tooltip("비용 한 줄 프리팹 (BuildingCostRow — BuildingInfoUI와 같은 프리팹을 쓴다). 두 비용 블록이 공유한다.")]
    [SerializeField] BuildingCostRow _costRowPrefab;
    [Tooltip("주민 증가 비용 박스의 헤더 라벨. 프리팹에 문구를 박지 않고 코드에서 로케일로 채운다. 비워도 동작한다.")]
    [SerializeField] TextMeshProUGUI _costHeaderText;

    [Header("본진 업그레이드 비용 (ScrollView)")]
    [Tooltip("본진 업그레이드 비용 Row들이 생성될 ScrollView의 Content Transform(#229). 주민 증가와 별개 축이라 블록을 따로 둔다.")]
    [SerializeField] Transform _upgradeCostContent;
    [Tooltip("업그레이드 비용 박스의 헤더 라벨. 비워도 동작한다.")]
    [SerializeField] TextMeshProUGUI _upgradeCostHeaderText;

    [Header("버튼")]
    [Tooltip("주민 증가 버튼. 최대 도달 시 라벨이 MAX로 바뀌고 비활성화된다.")]
    [SerializeField] Button _addVillagerButton;
    [Tooltip("주민 증가 버튼의 라벨(MAX 전환 표시용). 비워도 동작한다.")]
    [SerializeField] TextMeshProUGUI _addVillagerButtonText;
    [Tooltip("본진 업그레이드 버튼(#229). 최대 도달 시 라벨이 MAX로 바뀌고 비활성화된다.")]
    [SerializeField] Button _upgradeButton;
    [Tooltip("업그레이드 버튼의 라벨. 프리팹에 문구를 박지 않고 코드에서 로케일로 채운다.")]
    [SerializeField] TextMeshProUGUI _upgradeButtonText;

    // 중앙 모달이라 화면을 가리므로 명시적 닫기 버튼을 둔다(StorePanel과 같은 이유).
    // 우측 정보 패널 형태로 바꾸면 BuildingInfoUI처럼 선택 해제만으로 닫아도 되니 비워둬도 동작한다.
    [SerializeField] Button _closeButton;

    private BuildingAsset _building;
    private int _upgradeIndex = -1; // 업그레이드 트랙에서의 본진 index(미등록이면 -1) — BuildingInfoUI와 같은 캐시 패턴
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

        if (_addVillagerButton != null)
        {
            _addVillagerButton.onClick.AddListener(HandleAddVillagerClicked);
        }
        if (_upgradeButton != null)
        {
            _upgradeButton.onClick.AddListener(HandleUpgradeClicked);
        }
        if (_closeButton != null)
        {
            _closeButton.onClick.AddListener(HandleCloseClicked);
        }
        Hide(); // Instance 등록 후 숨기므로 안전
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }
        Unsubscribe();
        if (_addVillagerButton != null)
        {
            _addVillagerButton.onClick.RemoveListener(HandleAddVillagerClicked);
        }
        if (_upgradeButton != null)
        {
            _upgradeButton.onClick.RemoveListener(HandleUpgradeClicked);
        }
        if (_closeButton != null)
        {
            _closeButton.onClick.RemoveListener(HandleCloseClicked);
        }
        Instance = null;
    }

    // 본진 선택 시 호출(BuildingInfo.OnSelected). 주민 수·비용은 전부 컨트롤러에서 pull한다.
    public void Show(BuildingAsset building)
    {
        if (building == null || !EnsureController())
        {
            Hide();
            return;
        }

        _building = building;
        _upgradeIndex = _controller.UpgradeIndexOf(building); // 씬의 _upgradeBuildings에 본진이 등록돼야 0 이상(#229)
        Subscribe(); // 주민 증가·업그레이드·자원 변동·페이즈 전환으로 상태가 바뀌면 자동 갱신
        gameObject.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        Unsubscribe();
        ClearCostRows(_costContent);
        ClearCostRows(_upgradeCostContent);
        _building = null;
        _upgradeIndex = -1;
        gameObject.SetActive(false);
    }

    // 닫기는 Hide()를 직접 부르지 않고 선택 해제를 거친다. 표시만 내리고 MouseManager의 _selected를 남기면
    // 선택 아웃라인이 안 지워지고(OnSelectionChanged 미발행) 같은 건물을 재클릭해도 다시 열리지 않는다
    // (Select의 `_selected == next` 중복 제거가 삼킨다 — MouseManager.cs 주석이 금지하는 패턴, #228).
    // ClearSelection이 OnDeselected → BuildingInfo.HideAll로 이 패널까지 닫아주므로 Hide 호출은 불필요하다.
    private void HandleCloseClicked()
    {
        if (MouseManager.Instance != null)
        {
            MouseManager.Instance.ClearSelection();
        }
        else
        {
            Hide(); // 입력 매니저가 없는 부분 씬/테스트 폴백
        }
    }

    private void HandleAddVillagerClicked()
    {
        // 성공 시 컨트롤러 OnChanged → Refresh가 자동으로 다시 그린다(실패해도 무해 — 컨트롤러가 로그를 남긴다).
        if (_controller == null || _building == null)
        {
            return;
        }
        _controller.TryIncreaseVillagers(_building);
    }

    private void HandleUpgradeClicked()
    {
        // 성공 시 컨트롤러 OnChanged → Refresh가 자동으로 다시 그린다(실패해도 무해 — 컨트롤러가 로그를 남긴다).
        // 본진 레벨이 오르면 하위 건물 Max·연금술사 교환 배율도 같은 OnChanged로 따라 갱신된다(#229).
        if (_controller == null || _upgradeIndex < 0)
        {
            return;
        }
        _controller.TryUpgradeBuilding(_upgradeIndex);
    }

    private void Refresh()
    {
        if (_building == null || _controller == null)
        {
            return;
        }

        // 표시 레벨은 1부터(내부 0 = 미업그레이드). 본진이 _upgradeBuildings에 미등록이면 GetUpgradeLevel이
        // 0을 반환해 Lv 1로 고정된다 — 씬 배선 누락이 화면에 그대로 드러난다.
        int level = _controller.GetUpgradeLevel(_building) + 1;
        SetText(_titleText, $"{BuildingName()} ({L(k_LevelKey)} {level})");
        // 이 패널은 '보유 가능한 주민 수'를 늘리는 곳이라 총 보유량만 보여준다.
        // 배치 현황(AssignedTotal)은 경영 패널의 "n/m"이 담당한다 — 같은 수치를 두 곳에서 다른 의미로 쓰지 않는다.
        SetText(_villagerText, $"{L(k_MaxVillagersKey)} {_controller.MaxVillagers}");

        // 두 축은 서로 독립적이다 — 한쪽이 MAX여도 다른 쪽 버튼은 계속 눌린다.
        // 헤더는 어느 목록이 어느 버튼의 비용인지 못박는다("주민 증가 · 비용" / "업그레이드 · 비용").
        SetText(_costHeaderText, $"{L(k_AddVillagerKey)} · {L(k_CostKey)}");
        SetText(_upgradeCostHeaderText, $"{L(k_UpgradeKey)} · {L(k_CostKey)}");

        // 주민 증가: 비용 테이블 행을 모두 소진하면 null — 그게 곧 최대 주민 수 도달이다(상한 필드가 따로 없다).
        IReadOnlyList<ResourceCost> villagerCost = _controller.NextVillagerCost(_building);
        bool villagerMax = villagerCost == null;
        RebuildCostRows(_costContent, villagerCost);

        if (_addVillagerButton != null)
        {
            _addVillagerButton.interactable = !villagerMax && _controller.CanIncreaseVillagers(_building);
        }
        SetText(_addVillagerButtonText, villagerMax ? L(k_MaxKey) : L(k_AddVillagerKey));

        // 본진 업그레이드(#229): 미등록(-1)이면 비용이 null이라 자동으로 MAX 표시 + 비활성이 된다.
        // 본진은 자기 요구치로 잠기지 않으므로 여기서 null은 언제나 '진짜 최대'다(잠금 안내가 필요 없다).
        IReadOnlyList<ResourceCost> upgradeCost = _controller.UpgradeBuildingCost(_upgradeIndex);
        bool upgradeMax = upgradeCost == null;
        RebuildCostRows(_upgradeCostContent, upgradeCost);

        if (_upgradeButton != null)
        {
            _upgradeButton.interactable = _controller.CanUpgradeBuilding(_upgradeIndex);
        }
        SetText(_upgradeButtonText, upgradeMax ? L(k_MaxKey) : L(k_UpgradeKey));
    }

    private string BuildingName()
    {
        if (_building == null || _building.Data == null)
        {
            return "-";
        }
        return LocalizationHelper.Get(LocalizationHelper.k_BuildingsTable, _building.Data.NameKey);
    }

    // 비용 자원마다 Row 프리팹을 하나씩 생성한다. 각 Row는 자원별 보유량 대비 감당 여부로 초록/회색이 갈리므로
    // "나무는 되는데 철이 부족"한 상태가 한눈에 보인다. (BuildingInfoUI.RebuildCostRows와 같은 로직)
    //
    // StorePanelUI와 달리 매 갱신마다 다시 만들어도 안전하다 — 그쪽이 재생성을 피한 건 '행에 버튼이 있어서'
    // 클릭 처리 도중 그 버튼이 파괴되기 때문인데, 여기 액션 버튼은 패널의 고정 필드고 행은 텍스트뿐이다.
    private void RebuildCostRows(Transform content, IReadOnlyList<ResourceCost> costs)
    {
        ClearCostRows(content);
        if (costs == null || content == null || _costRowPrefab == null)
        {
            return;
        }

        foreach (ResourceCost c in costs)
        {
            if (c == null || c.Resource == null || c.Amount <= 0)
            {
                continue; // 미완성 authoring 행은 건너뛴다(BuildingAsset.OnValidate가 에디터에서 경고).
            }
            ResourceData data = ResolveData(c.Resource);
            if (data == null)
            {
                continue;
            }

            // 자원 종류는 아이콘으로만 표기한다 — Data는 지갑 대조(Kind)에만 쓰인다.
            bool affordable = _controller.ResourceCount(data.Kind) >= c.Amount;

            BuildingCostRow row = Instantiate(_costRowPrefab, content, false);
            row.Set(c.Resource.Icon, c.Amount, affordable);
        }
    }

    // Content의 기존 Row(프리팹에 남아 있는 예시 행 포함)를 모두 제거한다.
    private void ClearCostRows(Transform content)
    {
        if (content == null)
        {
            return;
        }
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Destroy(content.GetChild(i).gameObject);
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

    // NorthLand_default 키를 현재 로케일로 조회. BuildingInfoUI/StorePanelUI와 동일한 pull 방식(갱신마다 재조회).
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
            Debug.LogWarning("[CastlePanelUI] ManagementController를 찾지 못해 주민 정보를 표시할 수 없습니다.", this);
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
