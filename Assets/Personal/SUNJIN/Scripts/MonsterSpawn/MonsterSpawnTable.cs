using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MonsterSpawnTable : DataTable
{
    private readonly Dictionary<int, List<MonsterSpawnData>> table =
        new Dictionary<int, List<MonsterSpawnData>>();

    public override void Load(string filename)
    {
        table.Clear();

        string path = string.Format(FormatPath, filename);
        TextAsset textAsset = Resources.Load<TextAsset>(path);
        if (textAsset == null)
        {
            Debug.LogError($"MonsterSpawnTable: CSV file not found. path={path}");
            return;
        }

        List<MonsterSpawnData> list = LoadCSV<MonsterSpawnData>(textAsset.text);

        foreach (IGrouping<int, MonsterSpawnData> group in list.GroupBy(data => data.Round))
        {
            table[group.Key] = group.OrderBy(data => data.StartDelay).ToList();
        }
    }

    public List<MonsterSpawnData> GetRound(int round)
    {
        if (!table.TryGetValue(round, out List<MonsterSpawnData> spawnList))
        {
            return new List<MonsterSpawnData>();
        }

        return spawnList;
    }
}