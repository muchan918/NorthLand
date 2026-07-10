using UnityEngine;

public class ResourceTableTest : MonoBehaviour
{
    private void Start()
    {
        foreach (var id in new[] { "wood", "iron", "food", "mana" })
        {
            var data = DataTableManager.Get<ResourceTable>("ResourceTable").Get(id);
            Debug.Log($"{data.ResourceID} -> {data.DisplayName} ({data.Kind})");
        }
    }
}
