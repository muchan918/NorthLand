using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemyAsset))]
public class EnemyAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var enemyTypeProp = serializedObject.FindProperty("EnemyType");

        EditorGUILayout.PropertyField(serializedObject.FindProperty("EnemyID"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("icon"));
        EditorGUILayout.PropertyField(enemyTypeProp);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("MovementMode"));
        EditorGUILayout.Space();
   

        // enumValueIndex는 enum 선언 순서상의 인덱스. TowerAssetEditor와 동일한 주의사항:
        // EnemyType이 명시적 값 없이 선언 순서 그대로라 (int)value와 일치하지만,
        // 선언 순서가 바뀌거나 명시적 값이 추가되면 더 이상 일치하지 않으니 주의.
        var type = (EnemyType)enemyTypeProp.enumValueIndex;
        switch (type)
        {
            case EnemyType.Melee:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Melee"), true);
                break;
            case EnemyType.Ranged:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Ranged"), true);
                break;
            case EnemyType.Boss:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Boss"), true);
                break;
        }

        // 자폭(#453)은 EnemyType 축과 직교하므로 위 switch **밖에서** 항상 그린다.
        // switch 안에 넣으면 타입별 블록 3곳에 같은 줄을 복제해야 하고, 새 EnemyType이
        // 추가될 때 조용히 빠진다(그러면 저작할 수 없는 필드가 된다).
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("SelfDestruct"), true);

        serializedObject.ApplyModifiedProperties();
    }
}
