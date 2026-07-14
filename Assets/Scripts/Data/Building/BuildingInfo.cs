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

        BuildingInfoUI.Instance.ShowInfo(_buildingAsset.Data.Description);
    }

    public void OnDeselected()
    {
        BuildingInfoUI.Instance.HideInfo();
    }
}
