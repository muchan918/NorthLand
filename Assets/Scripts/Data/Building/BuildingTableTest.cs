using UnityEngine;

public class BuildingTableTest : MonoBehaviour
{
    private void Start()
    {
        foreach (var id in new[]
        {
            "woodcutter_house", "mine", "farm", "training_camp",
            "church", "headquarters", "alchemist_house", "magic_lab", "military_school",
        })
        {
            var data = DataTableManager.Get<BuildingTable>("BuildingTable").Get(id);
            Debug.Log($"{data.BuildingID} -> {data.NameKey} ({data.BuildingType}) : {data.RoleKey} / {data.DescriptionKey}");
        }
    }
}
