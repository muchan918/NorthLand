using System.IO;
using UnityEditor;
using UnityEngine;

public class TableImporter : EditorWindow
{
    private enum TableType
    {
        Resource,
        Building,
        Tower,
    }

    private TableType selectedTable = TableType.Resource;

    [MenuItem("Tools/Table Importer")]
    public static void Open()
    {
        GetWindow<TableImporter>("Table Importer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Table Importer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        selectedTable = (TableType)EditorGUILayout.EnumPopup("Table Type", selectedTable);
        EditorGUILayout.Space();

        if (GUILayout.Button("Import"))
        {
            Import(selectedTable);
        }
    }

    private void Import(TableType type)
    {
        switch (type)
        {
            case TableType.Resource:
                ImportResource();
                break;
            case TableType.Building:
                ImportBuilding();
                break;
            case TableType.Tower:
                ImportTower();
                break;
        }
    }

    private void ImportResource()
    {
        string csvPath = "Assets/Resources/DataTables/ResourceTable.csv";
        string csvText = File.ReadAllText(csvPath);
        var list = DataTable.LoadCSV<ResourceData>(csvText);

        string soFolder = "Assets/Resources/ScriptableObjects/Resources";

        if (!AssetDatabase.IsValidFolder("Assets/Resources/ScriptableObjects"))
            AssetDatabase.CreateFolder("Assets/Resources", "ScriptableObjects");
        if (!AssetDatabase.IsValidFolder(soFolder))
            AssetDatabase.CreateFolder("Assets/Resources/ScriptableObjects", "Resources");

        foreach (var data in list)
        {
            string assetPath = $"{soFolder}/{data.ResourceID}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ResourceAsset>(assetPath);

            if (existing != null)
            {
                existing.ResourceID = data.ResourceID;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                var so = CreateInstance<ResourceAsset>();
                so.ResourceID = data.ResourceID;
                AssetDatabase.CreateAsset(so, assetPath);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"ResourceTable Import 완료: {list.Count}개");
    }

    private void ImportBuilding()
    {
        string csvPath = "Assets/Resources/DataTables/BuildingTable.csv";
        string csvText = File.ReadAllText(csvPath);
        var list = DataTable.LoadCSV<BuildingData>(csvText);

        string soFolder = "Assets/Resources/ScriptableObjects/Buildings";

        if (!AssetDatabase.IsValidFolder("Assets/Resources/ScriptableObjects"))
            AssetDatabase.CreateFolder("Assets/Resources", "ScriptableObjects");
        if (!AssetDatabase.IsValidFolder(soFolder))
            AssetDatabase.CreateFolder("Assets/Resources/ScriptableObjects", "Buildings");

        foreach (var data in list)
        {
            string assetPath = $"{soFolder}/{data.BuildingID}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<BuildingAsset>(assetPath);

            if (existing != null)
            {
                existing.BuildingID = data.BuildingID;
                existing.BuildingType = data.BuildingType;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                var so = CreateInstance<BuildingAsset>();
                so.BuildingID = data.BuildingID;
                so.BuildingType = data.BuildingType;
                AssetDatabase.CreateAsset(so, assetPath);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"BuildingTable Import 완료: {list.Count}개");
    }

    private void ImportTower()
    {
        string csvPath = "Assets/Resources/DataTables/TowerTable.csv";
        string csvText = File.ReadAllText(csvPath);
        var list = DataTable.LoadCSV<TowerData>(csvText);

        string soFolder = "Assets/Resources/ScriptableObjects/Towers";

        if (!AssetDatabase.IsValidFolder("Assets/Resources/ScriptableObjects"))
            AssetDatabase.CreateFolder("Assets/Resources", "ScriptableObjects");
        if (!AssetDatabase.IsValidFolder(soFolder))
            AssetDatabase.CreateFolder("Assets/Resources/ScriptableObjects", "Towers");

        foreach (var data in list)
        {
            string assetPath = $"{soFolder}/{data.TowerID}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TowerAsset>(assetPath);

            if (existing != null)
            {
                existing.TowerID = data.TowerID;
                existing.TowerType = data.TowerType;
                existing.MagicEffectType = data.MagicEffectType;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                var so = CreateInstance<TowerAsset>();
                so.TowerID = data.TowerID;
                so.TowerType = data.TowerType;
                so.MagicEffectType = data.MagicEffectType;
                AssetDatabase.CreateAsset(so, assetPath);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"TowerTable Import 완료: {list.Count}개");
    }
}
