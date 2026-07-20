using UnityEngine;

public class BuildingTableTest : MonoBehaviour
{
    private void Start()
    {
        foreach (var id in new[]
        {
            "woodcutter_house", "mine", "farm",
            "castle", "alchemist_house", "magic_lab",
        })
        {
            var data = DataTableManager.Get<BuildingTable>("BuildingTable").Get(id);
            if (data == null) continue;

            Debug.Log($"{data.BuildingID} -> {data.NameKey} ({data.BuildingType}) : {data.RoleKey} / {data.DescriptionKey}");
        }
    }
}
