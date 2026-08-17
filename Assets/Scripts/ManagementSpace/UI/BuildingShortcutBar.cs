using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 생산라인 위 바로가기 버튼. 누르면 그 건물로 카메라를 옮기고 건물 패널을 연다.
public class BuildingShortcutBar : MonoBehaviour
{
    [Serializable]
    private struct Entry
    {
        public BuildingFocusPoint focus;
        public Button button;
    }

    [SerializeField] private Entry[] _entries;
    [SerializeField] private CameraController2 _camera;

    [Header("툴팁")]
    [SerializeField] private GameObject _tooltipPanel;
    [SerializeField] private RectTransform _tooltipRect;
    [SerializeField] private TextMeshProUGUI _tooltipText;
    [SerializeField] private Canvas _canvas;                      // 스케일 팩터 계산용
    [SerializeField] private Vector2 _cursorOffset = new(16f, -16f);

    private void Start()
    {
        if (_camera == null) _camera = FindFirstObjectByType<CameraController2>();

        HideTooltip();

        foreach (Entry entry in _entries)
        {
            if (entry.button == null) continue;

            // 씬에 없는 건물은 갈 곳이 없다.
            entry.button.interactable = entry.focus != null;
            entry.button.onClick.AddListener(() => Focus(entry));
        }
    }

    private void OnDestroy()
    {
        foreach (Entry entry in _entries)
        {
            if (entry.button != null) entry.button.onClick.RemoveAllListeners();
        }
    }

    private void LateUpdate()
    {
        if (_tooltipPanel != null && _tooltipPanel.activeSelf) FollowCursor();
    }

    // EventTrigger의 PointerEnter가 버튼 순번으로 호출한다.
    public void ShowTooltip(int index)
    {
        if (_tooltipPanel == null || index < 0 || index >= _entries.Length) return;

        _tooltipText.text = ResolveName(_entries[index].focus);
        _tooltipPanel.SetActive(true);
        FollowCursor(); // 첫 프레임부터 커서에 붙어 나오도록
    }

    // EventTrigger의 PointerExit가 호출한다.
    public void HideTooltip()
    {
        if (_tooltipPanel != null) _tooltipPanel.SetActive(false);
    }

    private void Focus(Entry entry)
    {
        if (entry.focus == null || _camera == null) return;

        _camera.MoveTo(entry.focus.FocusPosition);

        if (entry.focus.ZoomSize > 0f) _camera.ZoomTo(entry.focus.ZoomSize);
        if (entry.focus.Building != null) MouseManager.Instance?.SelectExternally(entry.focus.Building);
    }

    // 커서 위치는 MouseManager 경유로 얻는다 — Mouse.current 직접 폴링 금지(입력 단일 창구 계약).
    // _tooltipRect의 pivot이 좌상단(0,1)이라고 가정한다.
    private void FollowCursor()
    {
        MouseManager mouse = MouseManager.Instance;
        if (mouse == null || _tooltipRect == null) return;

        Vector2 pos = mouse.PointerPosition + _cursorOffset;
        float scale = _canvas != null ? _canvas.scaleFactor : 1f;
        Vector2 size = _tooltipRect.rect.size * scale;

        pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, Screen.width - size.x));
        pos.y = Mathf.Clamp(pos.y, size.y, Screen.height);
        _tooltipRect.position = pos;
    }

    // 언어가 바뀌어도 맞도록 호버할 때마다 조회한다.
    private static string ResolveName(BuildingFocusPoint focus)
    {
        BuildingAsset asset = focus != null && focus.Building != null ? focus.Building.Asset : null;
        if (asset == null) return string.Empty;

        BuildingTable table = DataTableManager.Get<BuildingTable>("BuildingTable");
        BuildingData data = table != null ? table.Get(asset.BuildingID) : null;

        return data != null
            ? LocalizationHelper.Get(LocalizationHelper.k_BuildingsTable, data.NameKey)
            : asset.BuildingID;
    }
}