using System;
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

    private void Start()
    {
        if (_camera == null) _camera = FindFirstObjectByType<CameraController2>();

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

    private void Focus(Entry entry)
    {
        if (entry.focus == null) return;
        if (_camera == null) return;

        _camera.MoveTo(entry.focus.FocusPosition);

        if (entry.focus.ZoomSize > 0f) _camera.ZoomTo(entry.focus.ZoomSize);
        if (entry.focus.Building != null) MouseManager.Instance?.SelectExternally(entry.focus.Building);
    }
}