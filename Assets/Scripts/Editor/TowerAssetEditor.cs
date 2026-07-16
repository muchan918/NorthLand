using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TowerAsset))]
public class TowerAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var towerTypeProp = serializedObject.FindProperty("TowerType");
        var magicEffectTypeProp = serializedObject.FindProperty("MagicEffectType");

        EditorGUILayout.PropertyField(serializedObject.FindProperty("TowerID"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("TowerPrefab"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("GhostPrefab"));
        EditorGUILayout.PropertyField(towerTypeProp);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("Cost"), true);
        EditorGUILayout.Space();

        // enumValueIndex는 enum 선언 순서상의 인덱스. BuildingAssetEditor와 동일한 주의사항:
        // TowerType/MagicEffectType이 명시적 값 없이 선언 순서 그대로라 (int)value와 일치하지만,
        // 선언 순서가 바뀌거나 명시적 값이 추가되면 더 이상 일치하지 않으니 주의.
        var type = (TowerType)towerTypeProp.enumValueIndex;
        switch (type)
        {
            case TowerType.Single:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Single"), true);
                break;
            case TowerType.Area:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Area"), true);
                break;
            case TowerType.Chain:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Chain"), true);
                break;
            case TowerType.Magic:
                EditorGUILayout.PropertyField(magicEffectTypeProp);
                EditorGUILayout.Space();

                var magicEffectType = (MagicEffectType)magicEffectTypeProp.enumValueIndex;
                switch (magicEffectType)
                {
                    case MagicEffectType.Buff:
                        EditorGUILayout.PropertyField(
                            serializedObject.FindProperty("Magic.BuffAura"), true);
                        break;
                    case MagicEffectType.Debuff:
                        EditorGUILayout.PropertyField(
                            serializedObject.FindProperty("Magic.DebuffAura"), true);
                        break;
                    case MagicEffectType.None:
                        EditorGUILayout.HelpBox(
                            "Magic 타입은 MagicEffectType을 Buff 또는 Debuff로 설정해야 합니다.",
                            MessageType.Warning);
                        break;
                }
                break;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
