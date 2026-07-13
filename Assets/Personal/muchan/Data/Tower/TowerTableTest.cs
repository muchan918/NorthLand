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
            Debug.Log($"{data.TowerID} -> {data.DisplayName} ({data.TowerType}/{data.MagicEffectType}) : {data.Role}");
        }
    }
}
