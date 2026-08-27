using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;

[CustomEditor(typeof(TutorialStepAsset))]
[CanEditMultipleObjects]
public class TutorialStepAssetEditor : Editor
{
    private const string KoreanTablePath = "Assets/Localization/NorthLand_Tutorial_ko-KR.asset";

    private static readonly LocalizedField[] LocalizedFields =
    {
        new LocalizedField("popupTitleKey", "Popup Title", 1),
        new LocalizedField("popupBodyKey", "Popup Body", 4),
        new LocalizedField("bubbleTextKey", "Bubble Text", 3)
    };

    private StringTable _koreanTable;

    private void OnEnable()
    {
        _koreanTable = AssetDatabase.LoadAssetAtPath<StringTable>(KoreanTablePath);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("한국어 문구 편집", EditorStyles.boldLabel);

        if (targets.Length != 1)
        {
            EditorGUILayout.HelpBox(
                "문구 편집은 TutorialStepAsset을 하나만 선택했을 때 사용할 수 있습니다.",
                MessageType.Info);
            return;
        }

        if (_koreanTable == null)
        {
            EditorGUILayout.HelpBox(
                $"한국어 튜토리얼 테이블을 찾을 수 없습니다.\n{KoreanTablePath}",
                MessageType.Error);
            return;
        }

        serializedObject.Update();

        foreach (LocalizedField field in LocalizedFields)
        {
            DrawLocalizedField(field);
        }
    }

    private void DrawLocalizedField(LocalizedField field)
    {
        SerializedProperty keyProperty = serializedObject.FindProperty(field.PropertyName);

        if (keyProperty == null)
        {
            EditorGUILayout.HelpBox(
                $"직렬화 필드 '{field.PropertyName}'을 찾을 수 없습니다.",
                MessageType.Error);
            return;
        }

        string key = keyProperty.stringValue?.Trim();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(field.Label, EditorStyles.miniBoldLabel);

        if (string.IsNullOrEmpty(key))
        {
            EditorGUILayout.HelpBox("키가 비어 있어 표시할 문구가 없습니다.", MessageType.None);
            return;
        }

        StringTableEntry entry = _koreanTable.GetEntry(key);

        if (entry == null)
        {
            EditorGUILayout.HelpBox(
                $"NorthLand_Tutorial 한국어 테이블에 키가 없습니다: {key}",
                MessageType.Warning);
            return;
        }

        EditorGUI.BeginChangeCheck();
        string value = EditorGUILayout.TextArea(
            entry.Value ?? string.Empty,
            GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * field.MinimumLines));

        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        Undo.RecordObject(_koreanTable, $"Edit Korean tutorial text: {key}");
        entry.Value = value;
        EditorUtility.SetDirty(_koreanTable);
        AssetDatabase.SaveAssetIfDirty(_koreanTable);
    }

    private readonly struct LocalizedField
    {
        public LocalizedField(string propertyName, string label, int minimumLines)
        {
            PropertyName = propertyName;
            Label = label;
            MinimumLines = minimumLines;
        }

        public string PropertyName { get; }

        public string Label { get; }

        public int MinimumLines { get; }
    }
}
