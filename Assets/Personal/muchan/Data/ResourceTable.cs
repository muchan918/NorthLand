using System.Collections.Generic;
using UnityEngine;

public class ResourceTable : DataTable
{
    private readonly Dictionary<string, ResourceData> table = new Dictionary<string, ResourceData>();

    public override void Load(string filename)
    {
        table.Clear();

        string path = string.Format(FormatPath, filename);
        TextAsset textAsset = Resources.Load<TextAsset>(path);
        List<ResourceData> list = LoadCSV<ResourceData>(textAsset.text);

        foreach (var data in list)
        {
            if (!table.TryAdd(data.ResourceID, data))
            {
                Debug.LogError($"ResourceID 중복: {data.ResourceID}");
            }
        }
    }

    public ResourceData Get(string id)
    {
        if (!table.TryGetValue(id, out var data))
        {
            Debug.LogError($"ResourceID 없음: {id}");
            return null;
        }
        return data;
    }
}
