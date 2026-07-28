using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 교환 상점 패널(연금술사의 집, #211). 지불 자원(마나석) 1종을 여러 자원으로 바꾸는 단방향 상점.
// 건물 선택 시 BuildingInfo가 열고, 교환 실행은 ManagementController.TryExchange에 위임한다(뷰는 로직 없음).
//
// BuildingInfoUI(정보 표시)와 같은 계보의 별도 씬 싱글톤이다 — 정보 패널을 재사용하지 않는 이유는
// 표시가 아니라 '행마다 액션 버튼이 있는 목록'이라 갱신 방식 자체가 다르기 때문.
// (주의) 이 오브젝트는 씬에서 '활성' 상태로 둬야 Awake가 실행되어 Instance가 등록된다. 인스펙터에서 미리 꺼두지 말 것.
public class StorePanelUI : MonoBehaviour
{
    public static StorePanelUI Instance { get; private set; }

    // NorthLand_default 스트링 테이블 키.
    private const string k_TitleKey = "store.title";
    private const string k_ExchangeKey = "store.exchange";
    private const string k_OwnedKey = "store.owned";

    [Tooltip("교환 데이터 소스. 비우면 씬에서 자동 탐색.")]
    [SerializeField] ManagementController _controller;

    [Header("StorePanel 연결")]
    [Tooltip("패널 제목 (건물명 — 교환소)")]
    [SerializeField] TextMeshProUGUI _titleText;
    [Tooltip("지불 자원 보유량 (예: 보유 마나석 30)")]
    [SerializeField] TextMeshProUGUI _balanceText;

    [Header("교환 목록 (ScrollView)")]
    [Tooltip("교환 Row들이 생성될 ScrollView의 Content Transform")]
    [SerializeField] Transform _offerContent;
    [Tooltip("교환 한 줄 프리팹 (StoreOfferRow)")]
    [SerializeField] StoreOfferRow _rowPrefab;

    [SerializeField] Button _closeButton;

    private BuildingAsset _building;
    private readonly List<RowBinding> _rows = new List<RowBinding>();
    private bool _subscribed;
    private ResourceTable _resourceTable; // 자원 표시명 해석용 Data 채움(호출부 채움 규약, SystemMap §2)

    // 생성된 행과 그 행이 대표하는 교환 항목. Refresh가 행을 다시 만들지 않고 이 쌍만 훑는다.
    private readonly struct RowBinding
    {
        public readonly StoreOfferRow Row;
        public readonly BuildingAsset.ExchangeOffer Offer;

        public RowBinding(StoreOfferRow row, BuildingAsset.ExchangeOffer offer)
        {
            Row = row;
            Offer = offer;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

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
        if (_closeButton != null)
        {
            _closeButton.onClick.RemoveListener(Hide);
        }
        Instance = null;
    }

    // 교환 건물 선택 시 호출(BuildingInfo.OnSelected). 교환 행은 여기서 한 번만 만들고,
    // 이후 자원 변동은 Refresh가 버튼 활성 상태만 갱신한다.
    public void Show(BuildingAsset building)
    {
        if (building == null || building.Exchange == null || building.Exchange.Offers == null || !EnsureController())
        {
            Hide();
            return;
        }

        _building = building;
        gameObject.SetActive(true);
        BuildRows();
        Subscribe(); // 교환·정산으로 지갑이 바뀌면 자동 갱신
        Refresh();
    }

    public void Hide()
    {
        Unsubscribe();
        ClearRows();
        _building = null;
        gameObject.SetActive(false);
    }

    // 교환 항목마다 Row 프리팹을 하나씩 생성해 ScrollView Content에 채운다.
    // BuildingInfoUI와 달리 매 갱신마다 다시 만들지 않는다 — 행에 버튼이 있어서,
    // 클릭으로 발생한 OnChanged가 그 버튼을 파괴해버리면 이벤트 처리 도중 뷰가 사라진다.
    private void BuildRows()
    {
        ClearRows();
        if (_offerContent == null || _rowPrefab == null)
        {
            Debug.LogWarning("[StorePanelUI] 교환 목록 Content 또는 Row 프리팹이 연결되지 않았습니다.", this);
            return;
        }

        string payName = ResolveName(_building.Exchange.PayResource);
        string buttonText = L(k_ExchangeKey);

        foreach (BuildingAsset.ExchangeOffer offer in _building.Exchange.Offers)
        {
            if (offer == null || offer.GainResource == null || offer.PayAmount <= 0 || offer.GainAmount <= 0)
            {
                continue; // 미완성 authoring 행은 건너뛴다(BuildingAsset.OnValidate가 에디터에서 경고).
            }

            StoreOfferRow row = Instantiate(_rowPrefab, _offerContent, false);
            BuildingAsset.ExchangeOffer captured = offer; // 클로저 캡처(루프 변수 캡처 함정 회피)
            row.Bind(payName, offer.PayAmount, ResolveName(offer.GainResource), offer.GainAmount,
                buttonText, () => HandleExchangeClicked(captured));
            _rows.Add(new RowBinding(row, offer));
        }
    }

    // Content의 기존 Row(프리팹에 남아 있는 예시 행 포함)를 모두 제거한다.
    private void ClearRows()
    {
        _rows.Clear();
        if (_offerContent == null)
        {
            return;
        }
        for (int i = _offerContent.childCount - 1; i >= 0; i--)
        {
            Destroy(_offerContent.GetChild(i).gameObject);
        }
    }

    private void HandleExchangeClicked(BuildingAsset.ExchangeOffer offer)
    {
        // 성공 시 컨트롤러 OnChanged → Refresh가 버튼 상태를 다시 계산한다(실패해도 무해).
        if (_controller == null || _building == null)
        {
            return;
        }
        _controller.TryExchange(_building, offer);
    }

    // 자원이 변할 때마다 호출된다. 행 구성은 그대로 두고 보유량 표시 + 버튼 활성 상태만 갱신한다.
    private void Refresh()
    {
        if (_building == null || _controller == null)
        {
            return;
        }

        SetText(_titleText, $"{BuildingName()} — {L(k_TitleKey)}");
        SetText(_balanceText, BalanceLine());

        for (int i = 0; i < _rows.Count; i++)
        {
            RowBinding binding = _rows[i];
            if (binding.Row == null)
            {
                continue;
            }
            binding.Row.SetInteractable(_controller.CanExchange(_building, binding.Offer));
        }
    }

    // "보유 마나석 30" — 지불 자원의 현재 보유량. 지불 자원 해석이 안 되면 빈 줄.
    private string BalanceLine()
    {
        ResourceData data = ResolveData(_building.Exchange.PayResource);
        if (data == null)
        {
            return string.Empty;
        }
        string name = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, data.NameKey);
        return $"{L(k_OwnedKey)} {name} {_controller.ResourceCount(data.Kind)}";
    }

    private string BuildingName()
    {
        if (_building == null || _building.Data == null)
        {
            return "-";
        }
        return LocalizationHelper.Get(LocalizationHelper.k_BuildingsTable, _building.Data.NameKey);
    }

    private string ResolveName(ResourceAsset resource)
    {
        ResourceData data = ResolveData(resource);
        return data != null ? LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, data.NameKey) : "?";
    }

    // ResourceAsset.Data 채움(호출부 채움 규약, SystemMap §2) — 자원 표시명 해석에 필요.
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

    // NorthLand_default 키를 현재 로케일로 조회. BuildingInfoUI와 동일한 pull 방식(갱신마다 재조회).
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
            Debug.LogWarning("[StorePanelUI] ManagementController를 찾지 못해 교환 목록을 표시할 수 없습니다.", this);
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
