using UnityEngine;

public class TowerTableTest : MonoBehaviour
{
    private void Start()
    {
        foreach (var id in new[]
        {
            "archer_tower", "cannon_tower", "lightning_tower", "haste_tower", "poison_tower",
        })
        {
            var data = DataTableManager.Get<TowerTable>("TowerTable").Get(id);
            if (data == null) continue;

            Debug.Log($"{data.TowerID} -> {LocalizationHelper.Get(LocalizationHelper.k_TowersTable, data.NameKey)} ({data.GridWidth}x{data.GridHeight}) : {LocalizationHelper.Get(LocalizationHelper.k_TowersTable, data.RoleKey)}");
        }
    }
}
