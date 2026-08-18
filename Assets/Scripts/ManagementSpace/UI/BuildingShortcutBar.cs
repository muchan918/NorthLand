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

    // 밤에 통째로 내리는 용도. SetActive가 아닌 이유는 ApplyPhase 주석 참고.
    [SerializeField] private CanvasGroup _group;

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

        if (DayNightManager.Instance == null)
        {
            Debug.LogError("[바로가기] DayNightManager를 찾을 수 없습니다 — 밤에도 패널이 열려 있게 됩니다.", this);
            return;
        }

        // OnDayStart는 부트스트랩(1일차)에도 오므로 낮 신호로 쓴다(PhasePanelSwitcher와 같은 구독).
        DayNightManager.Instance.OnDayStart += ShowDay;
        DayNightManager.Instance.OnDayToNight += ShowNight;

        ApplyPhase(DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day);
    }

    private void OnDestroy()
    {
        foreach (Entry entry in _entries)
        {
            if (entry.button != null) entry.button.onClick.RemoveAllListeners();
        }

        if (DayNightManager.Instance == null) return;

        DayNightManager.Instance.OnDayStart -= ShowDay;
        DayNightManager.Instance.OnDayToNight -= ShowNight;
    }

    private void ShowDay() => ApplyPhase(true);

    private void ShowNight() => ApplyPhase(false);

    // 밤에는 경영 공간에 갈 수 없다 — 전투 중 카메라가 마을로 순간이동하는 것을 막는다(GDD 밤 순간이동 방지).
    // SetActive가 아니라 CanvasGroup인 이유: 이 스크립트가 그 패널에 붙어 있어, 오브젝트를 끄면 아침에 스스로 켜지 못한다.
    private void ApplyPhase(bool day)
    {
        if (_group != null)
        {
            _group.alpha = day ? 1f : 0f;
            _group.interactable = day;
            _group.blocksRaycasts = day; // false면 EventTrigger의 PointerEnter 자체가 오지 않는다
        }

        // 툴팁은 UICanvas 직속이라 패널과 함께 사라지지 않는다 — 밤 전환 순간 커서가 버튼 위였으면 남는다.
        if (!day) HideTooltip();
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

    // 언어가 바뀌어도 맞도록 호버할 때마다 로컬라이즈를 다시 탄다.
    // 테이블 조회는 BuildingInfo.Awake가 Data에 캐시해 둔 것을 쓴다 — 여기서 또 Get 체인을 타지 않는다.
    private static string ResolveName(BuildingFocusPoint focus)
    {
        BuildingAsset asset = focus != null && focus.Building != null ? focus.Building.Asset : null;
        if (asset == null) return string.Empty;

        BuildingData data = asset.Data;

        return data != null
            ? LocalizationHelper.Get(LocalizationHelper.k_BuildingsTable, data.NameKey)
            : asset.BuildingID;
    }
}