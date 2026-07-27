using UnityEngine;

public class BuildingInfo : MonoBehaviour, ISelectable
{
    [SerializeField] private BuildingAsset _buildingAsset;

    // 교환 상점 건물(연금술사의 집)인지. 게이트를 BuildingType이 아니라 '교환 데이터 존재'로 거는 이유는
    // BuildingAsset.ValidateUpgradeCosts와 같다 — 타입은 인스펙터 authoring 분류일 뿐 동작 스위치가 아니다.
    private bool HasExchange => _buildingAsset.Exchange != null
                             && _buildingAsset.Exchange.Offers != null
                             && _buildingAsset.Exchange.Offers.Count > 0;

    private void Start()
    {
        _buildingAsset.Data = DataTableManager.Get<BuildingTable>("BuildingTable").Get(_buildingAsset.BuildingID);
    }

    public void OnSelected()
    {
        if (_buildingAsset.Data == null)
        {
            Debug.LogError($"BuildingInfo: Data 없음 ({_buildingAsset.BuildingID})", this);
            return;
        }

        // 건물 SO를 넘겨 패널이 이름·레벨·업그레이드/교환 정보를 컨트롤러에서 pull하도록 한다.
        // 두 패널은 서로 다른 싱글톤이라 겹쳐 뜰 수 있다 — 여는 쪽이 상대를 먼저 닫는다.
        if (HasExchange)
        {
            if (BuildingInfoUI.Instance != null) BuildingInfoUI.Instance.HideInfo();
            if (StorePanelUI.Instance != null)
            {
                StorePanelUI.Instance.Show(_buildingAsset);
            }
            else
            {
                Debug.LogWarning($"[BuildingInfo] {_buildingAsset.BuildingID}: 상점 패널이 씬에 없습니다(Imported 동기화 확인).", this);
            }
        }
        else
        {
            if (StorePanelUI.Instance != null) StorePanelUI.Instance.Hide();
            if (BuildingInfoUI.Instance != null) BuildingInfoUI.Instance.ShowInfo(_buildingAsset);
        }
    }

    public void OnDeselected()
    {
        if (HasExchange)
        {
            if (StorePanelUI.Instance != null) StorePanelUI.Instance.Hide();
        }
        else
        {
            if (BuildingInfoUI.Instance != null) BuildingInfoUI.Instance.HideInfo();
        }
    }
}
