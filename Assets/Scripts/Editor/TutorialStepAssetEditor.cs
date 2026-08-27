using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;

[CustomEditor(typeof(TutorialStepAsset))]
[CanEditMultipleObjects]
public class TutorialStepAssetEditor : Editor
{
    private const string SelectedLocalePreferenceKey = "NorthLand.TutorialStepAssetEditor.SelectedLocale";
    private const string KoreanTablePath = "Assets/Localization/NorthLand_Tutorial_ko-KR.asset";
    private const string EnglishTablePath = "Assets/Localization/NorthLand_Tutorial_en-US.asset";
    private const string JapaneseTablePath = "Assets/Localization/NorthLand_Tutorial_ja-JP.asset";

    private static readonly string[] LocaleToolbarLabels = { "한국어", "English", "日本語" };

    private static readonly LocalizedField[] LocalizedFields =
    {
        new LocalizedField("popupTitleKey", "Popup Title", 1),
        new LocalizedField("popupBodyKey", "Popup Body", 4),
        new LocalizedField("bubbleTextKey", "Bubble Text", 3)
    };

    private StringTable _koreanTable;
    private StringTable _englishTable;
    private StringTable _japaneseTable;
    private PreviewLocale _selectedLocale;

    private void OnEnable()
    {
        _koreanTable = AssetDatabase.LoadAssetAtPath<StringTable>(KoreanTablePath);
        _englishTable = AssetDatabase.LoadAssetAtPath<StringTable>(EnglishTablePath);
        _japaneseTable = AssetDatabase.LoadAssetAtPath<StringTable>(JapaneseTablePath);
        _selectedLocale = (PreviewLocale)Mathf.Clamp(
            EditorPrefs.GetInt(SelectedLocalePreferenceKey, (int)PreviewLocale.Korean),
            (int)PreviewLocale.Korean,
            (int)PreviewLocale.Japanese);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("다국어 문구 편집", EditorStyles.boldLabel);

        if (targets.Length != 1)
        {
            EditorGUILayout.HelpBox(
                "문구 편집은 TutorialStepAsset을 하나만 선택했을 때 사용할 수 있습니다.",
                MessageType.Info);
            return;
        }

        DrawLocaleToolbar();

        StringTable selectedTable = SelectedTable;

        if (selectedTable == null)
        {
            EditorGUILayout.HelpBox(
                $"{SelectedLocaleLabel} 튜토리얼 테이블을 찾을 수 없습니다.\n{SelectedTablePath}",
                MessageType.Error);
            return;
        }

        serializedObject.Update();

        foreach (LocalizedField field in LocalizedFields)
        {
            DrawLocalizedField(field, selectedTable);
        }
    }

    private void DrawLocaleToolbar()
    {
        EditorGUI.BeginChangeCheck();
        _selectedLocale = (PreviewLocale)GUILayout.Toolbar(
            (int)_selectedLocale,
            LocaleToolbarLabels);

        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetInt(SelectedLocalePreferenceKey, (int)_selectedLocale);
        }
    }

    private void DrawLocalizedField(LocalizedField field, StringTable table)
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

        StringTableEntry entry = table.GetEntry(key);

        if (entry == null)
        {
            EditorGUILayout.HelpBox(
                $"NorthLand_Tutorial {SelectedLocaleLabel} 테이블에 키가 없습니다: {key}",
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

        Undo.RecordObject(table, $"Edit {SelectedLocaleLabel} tutorial text: {key}");
        entry.Value = value;
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssetIfDirty(table);
    }

    private StringTable SelectedTable => _selectedLocale switch
    {
        PreviewLocale.Korean => _koreanTable,
        PreviewLocale.English => _englishTable,
        PreviewLocale.Japanese => _japaneseTable,
        _ => _koreanTable
    };

    private string SelectedTablePath => _selectedLocale switch
    {
        PreviewLocale.Korean => KoreanTablePath,
        PreviewLocale.English => EnglishTablePath,
        PreviewLocale.Japanese => JapaneseTablePath,
        _ => KoreanTablePath
    };

    private string SelectedLocaleLabel => LocaleToolbarLabels[(int)_selectedLocale];

    private enum PreviewLocale
    {
        Korean,
        English,
        Japanese
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
