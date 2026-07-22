using UnityEngine;

public class BuildingInfo : MonoBehaviour, ISelectable
{
    [SerializeField] private BuildingAsset _buildingAsset;

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

        // 건물 SO를 넘겨 패널이 이름·레벨·업그레이드 정보를 컨트롤러에서 pull하도록 한다.
        BuildingInfoUI.Instance.ShowInfo(_buildingAsset);
    }

    public void OnDeselected()
    {
        BuildingInfoUI.Instance.HideInfo();
    }
}
