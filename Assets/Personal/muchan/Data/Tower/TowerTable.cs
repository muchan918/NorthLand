using System.Collections.Generic;
using UnityEngine;

public class TowerTable : DataTable
{
    private readonly Dictionary<string, TowerData> table = new Dictionary<string, TowerData>();

    public override void Load(string filename)
    {
        table.Clear();

        string path = string.Format(FormatPath, filename);
        TextAsset textAsset = Resources.Load<TextAsset>(path);
        if (textAsset == null)
        {
            Debug.LogError($"CSV 파일을 찾을 수 없습니다: {path}");
            return;
        }

        List<TowerData> list = LoadCSV<TowerData>(textAsset.text);

        foreach (var data in list)
        {
            if (!table.TryAdd(data.TowerID, data))
            {
                Debug.LogError($"TowerID 중복: {data.TowerID}");
            }
        }
    }

    public TowerData Get(string id)
    {
        if (!table.TryGetValue(id, out var data))
        {
            Debug.LogError($"TowerID 없음: {id}");
            return null;
        }
        return data;
    }
}
