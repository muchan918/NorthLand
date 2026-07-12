using System.Collections.Generic;
using UnityEngine;

public static class DataTableManager
{
    private static readonly Dictionary<string, DataTable> tables =
        new Dictionary<string, DataTable>();

    static DataTableManager()
    {
        Init();
    }

    private static void Init()
    {
        var resourceTable = new ResourceTable();
        resourceTable.Load("ResourceTable");
        tables.Add("ResourceTable", resourceTable);

        var buildingTable = new BuildingTable();
        buildingTable.Load("BuildingTable");
        tables.Add("BuildingTable", buildingTable);
    }

    public static T Get<T>(string id)
        where T : DataTable
    {
        if (!tables.ContainsKey(id))
        {
            Debug.LogError("테이블 없음");
            return null;
        }

        return tables[id] as T;
    }
}
