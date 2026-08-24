using UnityEngine;

public class BuildingInfo : MonoBehaviour, ISelectable
{
    [SerializeField] private BuildingAsset _buildingAsset;

    /// 이 씬 인스턴스가 어느 건물인가. **씬에서 건물 종류를 되짚어야 하는 소비처**가 쓴다 —
    /// 컨트롤러는 건물을 SO로만 알기 때문에(`ManagementController.LineIndexOf`), 클릭·드롭처럼
    /// 월드에서 출발하는 경로는 여기서 SO로 건너와야 한다. 첫 사용처는 주민 드롭 배치(Resident.md §8)다.
    ///
    /// `BuildingInstanceRegistry`와 방향이 반대인 짝이다(그쪽은 SO → Transform).
    public BuildingAsset Asset => _buildingAsset;

    // 이 건물이 어느 패널을 여는지. 게이트를 BuildingType이 아니라 '데이터 존재'로 거는 이유는
    // BuildingAsset.ValidateUpgradeCosts와 같다 — 타입은 인스펙터 authoring 분류일 뿐 동작 스위치가 아니다.
    // (BuildingType.Castle이 있지만 여기서 쓰지 않는다 — 타입은 인스펙터가 그릴 필드만 정한다.)
    private bool HasExchange => _buildingAsset.Exchange != null
                             && _buildingAsset.Exchange.Offers != null
                             && _buildingAsset.Exchange.Offers.Count > 0;

    // 주민 수를 늘릴 수 있는 건물(본진)인지 — 비용 테이블 행이 하나라도 있으면 전용 패널을 연다(#227).
    private bool HasVillagerGrowth => _buildingAsset.Villager != null
                                   && _buildingAsset.Villager.Levels != null
                                   && _buildingAsset.Villager.Levels.Count > 0;

    // 선택 시 열리는 패널. 셋은 서로 배타적이며 동시에 뜨면 안 된다.
    private enum Panel { Info, Store, Castle }

    private void Start()
    {
        _buildingAsset.Data = DataTableManager.Get<BuildingTable>("BuildingTable").Get(_buildingAsset.BuildingID);
    }

    // 월드 좌표가 필요한 소비처(주민 퇴장 등)를 위해 자기 자리를 알린다 — 근거는 BuildingInstanceRegistry 참고.
    // Start가 아니라 OnEnable/OnDisable 짝으로 두는 이유: 등록/해제가 대칭이어야 건물이 꺼졌다 켜져도 어긋나지 않는다.
    private void OnEnable() => BuildingInstanceRegistry.Register(_buildingAsset, transform);

    private void OnDisable() => BuildingInstanceRegistry.Unregister(_buildingAsset, transform);

    public void OnSelected()
    {
        if (_buildingAsset.Data == null)
        {
            Debug.LogError($"BuildingInfo: Data 없음 ({_buildingAsset.BuildingID})", this);
            return;
        }

        // 건물 SO를 넘겨 패널이 이름·레벨·업그레이드/교환/주민 정보를 컨트롤러에서 pull하도록 한다.
        // 우선순위: 주민 증가(본진) → 교환(연금술사) → 기본 정보. 겹치는 건물이 생겨도 순서가 고정된다.
        if (HasVillagerGrowth)
        {
            ShowOnly(Panel.Castle);
        }
        else if (HasExchange)
        {
            ShowOnly(Panel.Store);
        }
        else
        {
            ShowOnly(Panel.Info);
        }
    }

    public void OnDeselected()
    {
        // 어느 패널이 열려 있었는지 알 필요 없이 전부 닫는다(분기 조건이 바뀌어도 닫기가 새지 않는다).
        HideAll();
    }

    // 패널 3개는 서로 다른 싱글톤이라 그냥 두면 겹쳐 뜬다 — 나머지를 먼저 닫고 대상만 연다.
    // 상호 배타를 이 한 곳에 모아두면 패널이 늘어도 '닫기 누락'이 구조적으로 생기지 않는다.
    private void ShowOnly(Panel target)
    {
        HideAll(target);

        // 패널이 정말로 열렸을 때만 소리를 낸다 — 배선이 빠진 씬에서 "소리는 났는데 화면은 그대로"가
        // 되면 원인이 사운드 쪽에 있는 것처럼 보여 진단이 한 단계 멀어진다.
        bool opened;

        switch (target)
        {
            case Panel.Castle:
                opened = CastlePanelUI.Instance != null;
                if (opened) CastlePanelUI.Instance.Show(_buildingAsset);
                else WarnMissingPanel("본진 패널");
                break;
            case Panel.Store:
                opened = StorePanelUI.Instance != null;
                if (opened) StorePanelUI.Instance.Show(_buildingAsset);
                else WarnMissingPanel("상점 패널");
                break;
            default:
                opened = BuildingInfoUI.Instance != null;
                if (opened) BuildingInfoUI.Instance.ShowInfo(_buildingAsset);
                else WarnMissingPanel("건물 정보 패널");
                break;
        }

        // 패널 오브젝트가 켜지는 쪽이 아니라 **클릭한 이 자리**에서 낸다(근거는 Sfx.PanelOpen 주석).
        // 같은 건물을 다시 클릭하면 MouseManager.Select의 중복 제거에 걸려 여기까지 오지 않는다 —
        // 패널이 이미 열려 있으니 "열리는 소리"가 다시 나지 않는 편이 맞다.
        if (opened) Sfx.PanelOpen();
    }

    // except로 지정한 패널만 남기고 전부 닫는다(인자 없이 부르면 전부 닫힌다).
    private static void HideAll(Panel? except = null)
    {
        if (except != Panel.Info && BuildingInfoUI.Instance != null) BuildingInfoUI.Instance.HideInfo();
        if (except != Panel.Store && StorePanelUI.Instance != null) StorePanelUI.Instance.Hide();
        if (except != Panel.Castle && CastlePanelUI.Instance != null) CastlePanelUI.Instance.Hide();
    }

    private void WarnMissingPanel(string panelName)
    {
        Debug.LogWarning($"[BuildingInfo] {_buildingAsset.BuildingID}: {panelName}이 씬에 없습니다(Imported 동기화 확인).", this);
    }
}
