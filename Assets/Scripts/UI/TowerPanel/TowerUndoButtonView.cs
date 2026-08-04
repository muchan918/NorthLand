using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

/// <summary>
/// 되돌리기 버튼(#281). 방금 한 타워 배치·합성을 하나씩 되돌린다. 밤에는 비활성으로 남는다.
/// </summary>
///
/// **선택 상태와 무관하게 상시 배치한다.** 되돌릴 대상은 "선택한 타워"가 아니라 "가장 최근 조작"이라
/// 인포 패널(<see cref="TowerInfoUI"/>)에 붙일 이유가 없고, 붙이면 아무것도 선택하지 않은 상태에서
/// 되돌릴 수 없게 된다 — 배치 직후가 정확히 그 상태다.
///
/// 배선 시점 규칙은 <see cref="ManagementEndDayConfirmPopup"/>과 같다: 자기완결적 배선(버튼 리스너)은
/// <c>Awake</c>, 다른 매니저에 의존하는 구독은 <c>Start</c>, 해제는 <c>OnDestroy</c>로 대칭.
public class TowerUndoButtonView : MonoBehaviour
{
    // 값은 NorthLand_default String Table(ko/en/ja)에 있다.
    private const string k_KeyUndo = "game.btn.undo";

    [Tooltip("되돌리기 버튼. 스택이 비었거나 밤이면 비활성화된다.")]
    [SerializeField] Button _button;

    private void Awake()
    {
        if (_button == null)
        {
            Debug.LogError("[되돌리기] 버튼이 연결되지 않았습니다 — 인스펙터에서 지정하세요.", this);
            return;
        }

        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener(HandleClick);
    }

    private void Start()
    {
        CommandHistory.OnChanged += Refresh;
        LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;

        if (DayNightManager.Instance != null)
        {
            DayNightManager.Instance.OnDayToNight += Refresh;
            DayNightManager.Instance.OnDayStart += Refresh;
        }
        else
        {
            Debug.LogWarning("[되돌리기] DayNightManager가 씬에 없어 페이즈 변화에 반응하지 않습니다.", this);
        }

        RefreshLabel();
        Refresh();
    }

    private void OnDestroy()
    {
        CommandHistory.OnChanged -= Refresh;
        LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;

        if (DayNightManager.Instance == null) return;
        DayNightManager.Instance.OnDayToNight -= Refresh;
        DayNightManager.Instance.OnDayStart -= Refresh;
    }

    private void HandleClick()
    {
        // 고스트를 들고 있으면 그것부터 치우고 끝낸다. 배치하려다 만 상태에서 엉뚱한 이전 조작이
        // 되감기면 "무엇을 되돌렸는지"가 화면에서 읽히지 않는다 — 한 번의 클릭은 한 가지만 한다.
        // 진짜로 되돌리려면 한 번 더 누른다.
        if (MouseManager.Instance != null && MouseManager.Instance.IsPlacing)
        {
            MouseManager.Instance.CancelPlacement();
            return;
        }

        // 되돌릴 타워가 선택 중이면 인포 패널·사거리 원이 파괴된 타워를 붙들고 남는다(WL-086 계열).
        MouseManager.Instance?.ClearSelection();
        CommandHistory.Undo();
    }

    // ⚠ 이 버튼은 정본 씬에서 **페이즈 패널 밖(UICanvas 직속)**에 있어 밤에도 꺼지지 않는다.
    // 따라서 "밤엔 되돌리기 불가"의 **유일한** 방어선은 CommandHistory.CanUndo의 페이즈 검사다 —
    // 그것을 중복으로 오해해 빼면 밤에 되돌리기가 열린다(밤엔 CommitAll로 스택이 비지만, 그건
    // 확정 타이밍에 기댄 우연이고 계약이 아니다). 버튼을 낮 패널 하위로 옮기면 그때 이중이 된다.
    private void Refresh()
    {
        if (_button != null) _button.interactable = CommandHistory.CanUndo;
    }

    // 로케일 전환에 반응해야 하는 지속형 표시라, LocalizationHelper.Get(pull 방식)을 그때마다 다시 부른다.
    private void HandleLocaleChanged(UnityEngine.Localization.Locale _) => RefreshLabel();

    private void RefreshLabel()
    {
        if (_button == null) return;

        TMP_Text label = _button.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, k_KeyUndo);
    }
}
