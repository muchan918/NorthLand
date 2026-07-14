using System.Collections.Generic;
using UnityEngine;

public class BuildingTable : DataTable
{
    private readonly Dictionary<string, BuildingData> table = new Dictionary<string, BuildingData>();

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

        List<BuildingData> list = LoadCSV<BuildingData>(textAsset.text);

        foreach (var data in list)
        {
            if (!table.TryAdd(data.BuildingID, data))
            {
                Debug.LogError($"BuildingID 중복: {data.BuildingID}");
            }
        }
    }

    public BuildingData Get(string id)
    {
        if (!table.TryGetValue(id, out var data))
        {
            Debug.LogError($"BuildingID 없음: {id}");
            return null;
        }
        return data;
    }
}
