using UnityEngine;

public class TowerTableTest : MonoBehaviour
{
    private void Start()
    {
        foreach (var id in new[]
        {
            "archer_tower", "cannon_tower", "lightning_tower", "haste_tower", "slow_tower",
        })
        {
            var data = DataTableManager.Get<TowerTable>("TowerTable").Get(id);
            if (data == null) continue;

            Debug.Log($"{data.TowerID} -> {data.DisplayName} ({data.TowerType}/{data.MagicEffectType}, {data.GridWidth}x{data.GridHeight}) : {data.Role}");
        }
    }
}
