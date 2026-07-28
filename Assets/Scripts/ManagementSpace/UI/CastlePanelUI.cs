using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 본진 전용 패널(#227). 주민 수 현황과 다음 증가 비용을 보여주고, 버튼으로 자원을 소모해 주민 수를 1 늘린다.
// 증가 로직·비용 차감은 ManagementController.TryIncreaseVillagers에 위임한다(뷰는 로직 없음).
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

    [Tooltip("주민·자원 상태 소스. 비우면 씬에서 자동 탐색.")]
    [SerializeField] ManagementController _controller;

    [Header("CastlePanel 연결")]
    [Tooltip("패널 제목 (건물명 Lv n)")]
    [SerializeField] TextMeshProUGUI _titleText;
    [Tooltip("주민 현황 (배치/총 보유)")]
    [SerializeField] TextMeshProUGUI _villagerText;

    [Header("주민 증가 비용 (ScrollView)")]
    [Tooltip("비용 Row들이 생성될 ScrollView의 Content Transform")]
    [SerializeField] Transform _costContent;
    [Tooltip("비용 한 줄 프리팹 (BuildingCostRow — BuildingInfoUI와 같은 프리팹을 쓴다)")]
    [SerializeField] BuildingCostRow _costRowPrefab;

    [Header("버튼")]
    [Tooltip("주민 증가 버튼. 최대 도달 시 라벨이 MAX로 바뀌고 비활성화된다.")]
    [SerializeField] Button _addVillagerButton;
    [Tooltip("주민 증가 버튼의 라벨(MAX 전환 표시용). 비워도 동작한다.")]
    [SerializeField] TextMeshProUGUI _addVillagerButtonText;
    [Tooltip("본진 업그레이드 버튼 — 자리만 확보한다(동작은 후속 이슈). 항상 비활성.")]
    [SerializeField] Button _upgradeButton;
    [Tooltip("업그레이드 버튼의 라벨. 프리팹에 문구를 박지 않고 코드에서 로케일로 채운다.")]
    [SerializeField] TextMeshProUGUI _upgradeButtonText;

    // 중앙 모달이라 화면을 가리므로 명시적 닫기 버튼을 둔다(StorePanel과 같은 이유).
    // 우측 정보 패널 형태로 바꾸면 BuildingInfoUI처럼 선택 해제만으로 닫아도 되니 비워둬도 동작한다.
    [SerializeField] Button _closeButton;

    private BuildingAsset _building;
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
        if (_closeButton != null)
        {
            _closeButton.onClick.AddListener(Hide);
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
        if (_closeButton != null)
        {
            _closeButton.onClick.RemoveListener(Hide);
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
        Subscribe(); // 주민 증가·자원 변동·페이즈 전환으로 상태가 바뀌면 자동 갱신
        gameObject.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        Unsubscribe();
        ClearCostRows();
        _building = null;
        gameObject.SetActive(false);
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

    private void Refresh()
    {
        if (_building == null || _controller == null)
        {
            return;
        }

        // 본진 레벨: 아직 업그레이드 트랙에 등록되지 않아 항상 0(=Lv 1)이지만, 후속 이슈에서 등록하면
        // 이 코드를 고치지 않아도 값이 따라 올라간다(GetUpgradeLevel은 미등록 건물에 0을 반환한다).
        int level = _controller.GetUpgradeLevel(_building) + 1;
        SetText(_titleText, $"{BuildingName()} ({L(k_LevelKey)} {level})");
        // 이 패널은 '보유 가능한 주민 수'를 늘리는 곳이라 총 보유량만 보여준다.
        // 배치 현황(AssignedTotal)은 경영 패널의 "n/m"이 담당한다 — 같은 수치를 두 곳에서 다른 의미로 쓰지 않는다.
        SetText(_villagerText, $"{L(k_MaxVillagersKey)} {_controller.MaxVillagers}");

        // 비용 테이블 행을 모두 소진하면 null — 그게 곧 최대 주민 수 도달이다(상한 필드가 따로 없다).
        IReadOnlyList<ResourceCost> cost = _controller.NextVillagerCost(_building);
        bool isMax = cost == null;
        if (isMax)
        {
            ClearCostRows();
        }
        else
        {
            RebuildCostRows(cost);
        }

        if (_addVillagerButton != null)
        {
            _addVillagerButton.interactable = !isMax && _controller.CanIncreaseVillagers(_building);
        }
        SetText(_addVillagerButtonText, isMax ? L(k_MaxKey) : L(k_AddVillagerKey));

        // 본진 업그레이드는 후속 이슈 — 자리만 보이고 누를 수 없다.
        if (_upgradeButton != null)
        {
            _upgradeButton.interactable = false;
        }
        SetText(_upgradeButtonText, L(k_UpgradeKey));
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
                continue; // 미완성 authoring 행은 건너뛴다(BuildingAsset.OnValidate가 에디터에서 경고).
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

    // Content의 기존 Row(프리팹에 남아 있는 예시 행 포함)를 모두 제거한다.
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
