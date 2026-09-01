using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

/// <summary>
/// 되돌리기 버튼(#281 → #444). 방금 한 조작을 하나씩 되돌린다 — 타워 배치·합성과 <b>건물 업그레이드</b>가
/// 같은 스택에 쌓이므로 누른 역순으로 되감긴다. 밤에는 비활성으로 남는다.
/// </summary>
///
/// ⚠ **클래스 이름은 잔재다.** 되돌리는 대상이 타워만이 아니게 됐지만(#444), 이름을 바꾸면 파일명·클래스명이
/// 함께 바뀌어야 하고 정본 씬·프리팹 10여 곳의 `m_EditorClassIdentifier`가 어긋난 채 남는다(GUID로 해석되므로
/// 동작은 하지만 재직렬화까지 diff가 지저분해진다). 에디터를 띄운 채 정리하는 편이 안전해 이름은 그대로 뒀다.
///
/// **선택 상태와 무관하게 상시 배치한다.** 되돌릴 대상은 "선택한 타워"가 아니라 "가장 최근 조작"이라
/// 인포 패널(<see cref="TowerInfoUI"/>)에 붙일 이유가 없고, 붙이면 아무것도 선택하지 않은 상태에서
/// 되돌릴 수 없게 된다 — 배치 직후가 정확히 그 상태다.
///
/// **요청 해석은 이 클래스에 없다** — 규칙(고스트를 먼저 치운다 등)은 <see cref="UndoRequest"/>가 갖는다.
/// 그래야 Ctrl+Z가 같은 규칙을 쓰고, 이 버튼이 씬에 없어도 단축키가 동작한다.
///
/// 배선 시점 규칙은 <see cref="ManagementEndDayConfirmPopup"/>과 같다: 자기완결적 배선(버튼 리스너)은
/// <c>Awake</c>, 다른 매니저에 의존하는 구독은 <c>Start</c>, 해제는 <c>OnDestroy</c>로 대칭.
public class TowerUndoButtonView : MonoBehaviour, IDisabledClickFeedback
{
    // 값은 NorthLand_default String Table(ko/en/ja)에 있다.
    private const string k_KeyUndo = "game.btn.undo";

    [Tooltip("되돌리기 버튼. 스택이 비었거나 밤이거나 현재 튜토리얼 단계에서 허용되지 않으면 비활성화된다.")]
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

        // 되돌리기음·거절음은 UndoRequest가 낸다 — 공용 클릭음까지 겹치지 않게 뺀다.
        UiClickSfxIgnore.ApplyTo(_button);
    }

    private void Start()
    {
        CommandHistory.OnChanged += Refresh;
        TutorialInputGate.Changed += Refresh;
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
        TutorialInputGate.Changed -= Refresh;
        LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;

        if (DayNightManager.Instance == null) return;
        DayNightManager.Instance.OnDayToNight -= Refresh;
        DayNightManager.Instance.OnDayStart -= Refresh;
    }

    // 클릭과 Ctrl+Z는 **같은 진입점**을 쓴다 — 두 벌로 갈라지면 한쪽만 고쳐진다.
    private void HandleClick() => UndoRequest.Submit();

    /// 되돌릴 것이 없어(또는 밤·튜토리얼 제한으로) 버튼이 회색인 채 눌렸다.
    ///
    /// **이 경로가 없으면 「되돌릴 것 없음 → 거절음」 계약이 버튼에서 도달 불가다.** 그 소리는
    /// `UndoRequest.Submit`의 else 분기에 있는데, Refresh가 `CanUndo == false`일 때 `interactable`을
    /// 내려 버려 `onClick`이 아예 발화하지 않는다 — `UndoRequest`의 "⚠ Ctrl+Z는 언제든 눌린다" 주석이
    /// 가리키는 비대칭의 나머지 절반이고, 실제로 단축키로만 들렸다. `UiClickSfxIgnore`가 걸려 있어
    /// 공용 클릭음도 나지 않으므로 버튼은 완전한 무음이었다(#550).
    ///
    /// ⚠ **Submit을 부르지 않는다.** 그쪽은 상태를 바꾸는 경로이고, 이 훅은 소리·연출만 허용된다
    /// (`IDisabledClickFeedback`의 제약 — 팀 계약 #1 넷째 예외의 전제). 어차피 비활성 사유가
    /// 「되돌릴 것 없음」이라 되돌릴 것도 없다.
    public void OnDisabledClick(Selectable pressed) => Sfx.Rejected();

    // ⚠ 이 버튼은 정본 씬에서 **페이즈 패널 밖(UICanvas 직속)**에 있어 밤에도 꺼지지 않는다.
    // 따라서 "밤엔 되돌리기 불가"의 **유일한** 방어선은 CommandHistory.CanUndo의 페이즈 검사다 —
    // 그것을 중복으로 오해해 빼면 밤에 되돌리기가 열린다(밤엔 CommitAll로 스택이 비지만, 그건
    // 확정 타이밍에 기댄 우연이고 계약이 아니다). 버튼을 낮 패널 하위로 옮기면 그때 이중이 된다.
    private void Refresh()
    {
        if (_button != null)
        {
            _button.interactable = CommandHistory.CanUndo
                && TutorialInputGate.AllowsForDisplay(TutorialAction.Undo);
        }
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
