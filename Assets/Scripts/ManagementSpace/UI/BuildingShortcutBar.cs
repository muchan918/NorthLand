using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// 생산라인 위 바로가기 버튼. 누르면 그 건물로 카메라를 옮기고 건물 패널을 연다.
public class BuildingShortcutBar : MonoBehaviour
{
    private static readonly Key[] k_ShortcutKeys =
    {
        Key.Digit1,
        Key.Digit2,
        Key.Digit3,
        Key.Digit4,
        Key.Digit5,
        Key.Digit6,
        Key.Digit7,
        Key.Digit8,
        Key.Digit9
    };

    [Serializable]
    private struct Entry
    {
        public BuildingFocusPoint focus;
        public Button button;

        [Tooltip("건물이 없는 목적지의 로컬라이제이션 키")]
        public string fallbackNameKey;
    }

    /// 바로가기 버튼으로 건물에 갔다. 인자는 그 건물의 SO(초점에 건물이 없으면 null).
    ///
    /// Focus가 부르는 MouseManager.SelectExternally는 OnPrimarySelect로 흘러가 **건물을 직접 클릭한 것과
    /// 똑같이 보인다**. "바로가기를 썼다"를 구분해야 하는 소비처(튜토리얼의 조작 학습 판정)는 이 통지를 봐야 한다.
    public event Action<BuildingAsset> Focused;

    [SerializeField] private Entry[] _entries;
    [SerializeField] private CameraController2 _camera;
    [SerializeField] private SettingUI _settingUI;

    [Header("툴팁")]
    [SerializeField] private GameObject _tooltipPanel;
    [SerializeField] private RectTransform _tooltipRect;
    [SerializeField] private TextMeshProUGUI _tooltipText;
    [SerializeField] private Canvas _canvas;                      // 스케일 팩터 계산용
    [SerializeField] private Vector2 _cursorOffset = new(16f, -16f);

    // KeyboardManager의 바인딩 목록은 static이라, 해제할 때 같은 Action 인스턴스가 필요하다.
    private Action[] _shortcutHandlers;

    private void Start()
    {
        if (_camera == null)
            _camera = FindFirstObjectByType<CameraController2>();

        if (_settingUI == null)
            _settingUI = FindFirstObjectByType<SettingUI>();

        HideTooltip();

        foreach (Entry entry in _entries)
        {
            if (entry.button == null)
                continue;

            entry.button.interactable = entry.focus != null;
            entry.button.onClick.AddListener(() => Focus(entry));
        }

        BindShortcuts();
    }

    private void OnDestroy()
    {
        UnbindShortcuts();

        foreach (Entry entry in _entries)
        {
            if (entry.button != null)
                entry.button.onClick.RemoveAllListeners();
        }
    }

    private void BindShortcuts()
    {
        int count = Mathf.Min(_entries.Length, k_ShortcutKeys.Length);
        _shortcutHandlers = new Action[count];

        for (int i = 0; i < count; i++)
        {
            int index = i;
            Action handler = () => FocusEntry(index);
            _shortcutHandlers[index] = handler;

            KeyboardManager.Bind(
                k_ShortcutKeys[index],
                KeyModifier.None,
                handler,
                $"건물 바로가기 {index + 1}");
        }
    }

    private void UnbindShortcuts()
    {
        if (_shortcutHandlers == null) return;

        for (int i = 0; i < _shortcutHandlers.Length; i++)
        {
            Action handler = _shortcutHandlers[i];
            if (handler == null) continue;

            KeyboardManager.Unbind(k_ShortcutKeys[i], KeyModifier.None, handler);
        }

        _shortcutHandlers = null;
    }

    private void FocusEntry(int index)
    {
        // 설정창은 뒤 월드 입력을 막는다. 버튼 클릭은 패널의 Raycast가 담당하고,
        // 키보드 경로는 여기서 같은 정책을 적용한다.
        if (_settingUI != null && _settingUI.IsOpen)
            return;

        if (index < 0 || index >= _entries.Length)
            return;

        Entry entry = _entries[index];

        if (entry.focus == null)
            return;

        Focus(entry);
    }

    private void LateUpdate()
    {
        if (_tooltipPanel != null && _tooltipPanel.activeSelf) FollowCursor();
    }

    // EventTrigger의 PointerEnter가 버튼 순번으로 호출한다.
    public void ShowTooltip(int index)
    {
        if (_tooltipPanel == null || index < 0 || index >= _entries.Length) return;

        _tooltipText.text = ResolveName(_entries[index]);
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
        // 키보드 숫자키와 UI 버튼이 모두 이 경로로 들어오므로 한 곳에서 막는다.
        // 이후 단축키 학습 단계는 SO에서 UseBuildingShortcut을 허용하면 그대로 열린다.
        if (!TutorialInputGate.Allows(TutorialAction.UseBuildingShortcut))
            return;

        if (entry.focus == null || _camera == null)
            return;

        _camera.MoveTo(entry.focus.FocusPosition);

        if (entry.focus.ZoomSize > 0f)
            _camera.ZoomTo(entry.focus.ZoomSize);

        if (entry.focus.Building != null)
        {
            // 건물 바로가기: 해당 건물을 선택하고 패널을 연다.
            MouseManager.Instance?.SelectExternally(
                entry.focus.Building);
        }
        else
        {
            // 배틀맵 바로가기: 현재 선택 및 패널을 닫는다.
            MouseManager.Instance?.CancelInteractions();
        }

        Focused?.Invoke(entry.focus.Building != null? entry.focus.Building.Asset: null);
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

    // 언어가 바뀌어도 맞도록 호버할 때마다 로컬라이즈를 다시 탄다.
    // 테이블 조회는 BuildingInfo.Awake가 Data에 캐시해 둔 것을 쓴다 — 여기서 또 Get 체인을 타지 않는다.
    private static string ResolveName(Entry entry)
    {
        BuildingFocusPoint focus = entry.focus;

        BuildingAsset asset = focus != null && focus.Building != null ? focus.Building.Asset : null;

        if (asset == null)
        {
            return string.IsNullOrEmpty(entry.fallbackNameKey)
                ? string.Empty
                : LocalizationHelper.Get(LocalizationHelper.k_DefaultTable,entry.fallbackNameKey);
        }

        BuildingData data = asset.Data;

        return data != null
            ? LocalizationHelper.Get(LocalizationHelper.k_BuildingsTable,data.NameKey) : asset.BuildingID;
    }
}
