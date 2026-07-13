using System.Collections.Generic;
using UnityEngine;

public class EnemyTable : DataTable
{
    private readonly Dictionary<string, EnemyData> table = new Dictionary<string, EnemyData>();

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

        List<EnemyData> list = LoadCSV<EnemyData>(textAsset.text);

        foreach (var data in list)
        {
            if (!table.TryAdd(data.EnemyID, data))
            {
                Debug.LogError($"EnemyID 중복: {data.EnemyID}");
            }
        }
    }

    public EnemyData Get(string id)
    {
        if (!table.TryGetValue(id, out var data))
        {
            Debug.LogError($"EnemyID 없음: {id}");
            return null;
        }
        return data;
    }
}
