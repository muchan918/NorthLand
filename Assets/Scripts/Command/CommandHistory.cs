using System;
using System.Collections.Generic;
using UnityEngine;

/// 낮 동안 한 되돌릴 수 있는 조작의 LIFO 히스토리(#281). 밤으로 넘어가면 전부 확정하고 비운다.
///
/// **왜 static인가**: 되돌리기는 씬에 실체를 둘 이유가 없는 순수 상태다(그릴 것도, 인스펙터로 조절할
/// 것도 없다). MonoBehaviour로 만들면 씬마다 배선해야 하고 빠뜨리면 되돌리기가 **조용히** 죽는다 —
/// `GroupSelectableRegistry`가 같은 이유로 static이다.
///
/// **왜 LIFO인가**: 편의가 아니라 정확성 요건이다. 근거는 `IReversibleCommand` 주석 참고 — 요약하면
/// 되돌린 타워가 자기 타일을 되찾을 수 있으려면 그 사이에 그 타일을 점유한 조작이 먼저 풀려야 한다.
///
/// Redo는 없다. 되돌린 조작을 다시 실행하려면 커맨드가 "실행 전 상태"를 한 벌 더 들고 있어야 하는데,
/// 지금 커맨드들은 그걸 안 들고 있다(합성은 재료를 되살리고 잊는다). 필요해지면 그때 넣는다.
public static class CommandHistory
{
    /// 스택 깊이 상한. 넘치는 만큼은 **아래(가장 오래된 것)부터 확정**한다 — 아래 Push 참고.
    public const int k_MaxDepth = 20;

    private static readonly List<IReversibleCommand> _stack = new();

    // 어느 DayNightManager에 붙어 있는가. "구독했는가"(bool)가 아닌 이유는 아래 EnsureSubscribed 참고.
    private static DayNightManager _subscribedTo;

    /// 스택이 바뀌었다(Push/Undo/CommitAll). 되돌리기 버튼이 활성 상태를 갱신하는 신호.
    public static event Action OnChanged;

    /// 지금 되돌릴 수 있는가. 밤이면 스택이 비어 있는 것이 정상이지만, 페이즈를 함께 보는 것이
    /// "밤엔 되돌리기 불가"를 이 클래스의 계약으로 만든다(UI가 잊어도 지켜진다).
    ///
    /// ⚠ **조회에 부작용이 있다**(`EnsureSubscribed`). 그 판정 — "지금 씬의 DayNightManager가
    /// 내가 구독한 그것인가" — 이 여기서도 돌아야 **상태와 표시가 어긋나지 않는다.** 정리 트리거가
    /// `Push`에만 있으면 씬을 다시 로드한 뒤 죽은 커맨드가 스택에 남아 버튼이 활성으로 보이고,
    /// 눌러도 대상이 전부 파괴돼 있어 아무 일도 일어나지 않는다(최대 k_MaxDepth번).
    /// 호출은 멱등하고 비용은 인스턴스 조회 1회 + 참조 비교 1회라, 버튼이 매 갱신에 불러도 무해하다.
    public static bool CanUndo
    {
        get
        {
            EnsureSubscribed();
            return _stack.Count > 0 && IsDay;
        }
    }

    public static int Count => _stack.Count;

    // DayNightManager가 씬에 없으면 낮으로 간주한다 — 경영/페이즈 없는 테스트 씬에서도 되돌리기가
    // 동작하게 하는 permissive 관용(ManagementController.IsDay와 같은 판단).
    private static bool IsDay =>
        DayNightManager.Instance == null || DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day;

    /// 플레이 세션 시작마다 비운다. 지금은 도메인 리로드가 켜져 있어 static이 자동 초기화되지만,
    /// 팀이 "Enter Play Mode without domain reload"를 켜는 순간 이전 세션의 죽은 커맨드가 그대로 남는다
    /// (`GroupSelectableRegistry`와 같은 대비).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _stack.Clear();
        _subscribedTo = null;
        OnChanged = null;
    }

    /// 성공으로 끝난 커맨드를 히스토리에 올린다. **`Confirm()`은 여기서 부른다** — 호출부가
    /// "확정하고 등록"을 두 줄로 나눠 쓰면 언젠가 한쪽을 빠뜨린다(등록 안 된 커맨드는 밤에도
    /// 확정되지 않아 소프트 소모물이 영원히 비활성으로 남는다).
    public static void Push(IReversibleCommand command)
    {
        if (command == null) return;

        EnsureSubscribed();
        command.Confirm();
        _stack.Add(command);

        // 상한 초과분은 **버리는 게 아니라 Commit한다.** 그냥 목록에서 빼면 합성 재료가 비활성
        // (소프트 소모) 상태로 씬에 영원히 남는다 — 소프트 소모의 짝이 사라지는 자리다.
        while (_stack.Count > k_MaxDepth)
        {
            _stack[0].Commit();
            _stack.RemoveAt(0);
        }

        OnChanged?.Invoke();
    }

    /// 가장 최근 조작을 되돌린다. 밤이거나 스택이 비었으면 아무 일도 하지 않는다.
    public static void Undo()
    {
        if (!CanUndo) return;

        int last = _stack.Count - 1;
        IReversibleCommand command = _stack[last];
        _stack.RemoveAt(last);
        command.Undo();

        OnChanged?.Invoke();
    }

    /// 히스토리 전체를 확정하고 비운다(밤 진입). 이 뒤로는 되돌릴 수 없다.
    ///
    /// **진행 중(Executed·미확정) 커맨드는 여기 없다** — 그건 `PhasePanelSwitcher.ShowNight`의
    /// 배치 취소가 `Undo`로 끝낸다. 두 집합이 겹치지 않으므로 `OnDayToNight` 구독 순서가 어느 쪽이든
    /// 결과가 같다. 순서를 강제하는 장치를 두지 않는 근거가 이것이다.
    ///
    /// 오래된 것부터 Commit한다 — Commit끼리는 서로 간섭하지 않지만(각자 자기 소프트 소모물만 파괴),
    /// 되돌리기가 LIFO인 것과 대칭을 이루도록 쌓인 순서를 따른다.
    public static void CommitAll()
    {
        if (_stack.Count == 0) return;

        foreach (IReversibleCommand command in _stack) command.Commit();
        _stack.Clear();

        OnChanged?.Invoke();
    }

    /// 밤 진입 확정을 자기 자신에게 건다. **등록(`Push`)과 조회(`CanUndo`) 양쪽에서 부른다** — 등록만
    /// 걸면 정리 시점이 "다음 조작"으로 밀려 그 사이 버튼이 거짓 활성으로 보인다(CanUndo 주석 참고).
    ///
    /// **"구독했는가"가 아니라 "누구에게 구독했는가"로 판정한다** —
    /// `DayNightManager`는 씬마다 새 인스턴스라, bool 빗장이면 씬을 다시 로드했을 때 죽은 인스턴스의
    /// 이벤트를 붙들고 있게 되고 밤이 와도 확정이 안 걸린다. 인스턴스가 바뀌었으면 스택도 비운다 —
    /// 이전 씬의 커맨드는 대상 GameObject가 이미 없다.
    private static void EnsureSubscribed()
    {
        DayNightManager current = DayNightManager.Instance;
        if (current == _subscribedTo) return;

        if (_subscribedTo != null) _subscribedTo.OnDayToNight -= CommitAll;
        _stack.Clear();
        _subscribedTo = current;

        if (current != null) current.OnDayToNight += CommitAll;
        else Debug.LogWarning("[되돌리기] DayNightManager가 씬에 없어 밤 진입 확정이 걸리지 않습니다 — 되돌리기 자체는 동작합니다.");
    }
}
