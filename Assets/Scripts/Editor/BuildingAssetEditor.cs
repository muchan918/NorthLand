using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BuildingAsset))]
public class BuildingAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var buildingTypeProp = serializedObject.FindProperty("BuildingType");

        EditorGUILayout.PropertyField(serializedObject.FindProperty("BuildingID"));
        EditorGUILayout.PropertyField(buildingTypeProp);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("Cost"), true);
        EditorGUILayout.Space();

        // enumValueIndex는 enum 선언 순서상의 인덱스. BuildingType이 명시적 값 없이
        // 선언 순서(Production=0, General=1, Skill=2, Store=3) 그대로라 (int)value와 일치하지만,
        // 선언 순서가 바뀌거나 명시적 값이 추가되면 더 이상 일치하지 않으니 주의.
        var type = (BuildingType)buildingTypeProp.enumValueIndex;
        switch (type)
        {
            case BuildingType.Production:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Production"), true);
                break;
            case BuildingType.Skill:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Skill"), true);
                break;
            case BuildingType.Store:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Exchange"), true);
                break;
            case BuildingType.General:
                EditorGUILayout.HelpBox("General 타입은 추가 필드가 없습니다.", MessageType.None);
                break;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
