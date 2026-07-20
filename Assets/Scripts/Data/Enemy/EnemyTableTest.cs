using UnityEngine;

public class EnemyTableTest : MonoBehaviour
{
    private void Start()
    {
        foreach (var id in new[]
        {
            "goblin_warrior", "goblin_archer", "ogre_king",
        })
        {
            var data = DataTableManager.Get<EnemyTable>("EnemyTable").Get(id);
            if (data == null) continue;

            Debug.Log($"{data.EnemyID} -> {LocalizationHelper.Get(LocalizationHelper.k_EnemiesTable, data.NameKey)} ({data.EnemyType}) : {LocalizationHelper.Get(LocalizationHelper.k_EnemiesTable, data.RoleKey)}");
        }
    }
}
