using System.Collections.Generic;

/// <see cref="IReversibleCommand"/> 4단 트랜잭션의 공통 기반 — 상태 전이와 비용 환원을 한 군데로 모았다(#281 → #444).
///
/// **왜 승격했는가**: 되돌릴 수 있는 조작이 타워 둘(배치·합성)이던 동안은 각자 같은 모양의 `enum State`와
/// 같은 모양의 가드를 들고 있어도 견딜 만했다. 경영(건물 업그레이드)이 들어오면서 셋이 되는데, 이 계약에서
/// 가장 조용한 고장은 **그 셋이 조금씩 달라지는 것**이다 — 예를 들어 "`Commit`은 `Confirmed`만 받는다"가
/// 한 군데서 어긋나면 밤 진입이 진행 중인 세션의 재료를 먼저 파괴해 그 세션의 취소가 되살릴 것을 잃는다
/// (`TowerMergeCommand.OnCommit`의 ⚠ 참고). 그래서 **상태 전이는 이 클래스에서만** 한다.
///
/// **비용 환원도 여기 있다.** "자원을 소모했다"가 되돌릴 수 있는 조작의 공통 모양이라(#444), 실지불 비용을
/// 100% 되돌리는 것은 파생이 각자 들고 있을 코드가 아니다. 소모가 없는 커맨드는 `(null, null)`을 넘긴다.
///
/// 파생은 `On*` 훅만 채운다 — 훅이 불렸다는 것 자체가 상태 가드를 통과했다는 뜻이므로 훅 안에서 상태를
/// 다시 검사하지 않는다.
public abstract class ReversibleCommandBase : IReversibleCommand
{
    protected enum State { Pending, Executed, Confirmed, Committed, Undone }

    // 실지불 비용과 그 환원 창구. 무료 조작(경영 없는 테스트 씬·소모가 없는 합성)은 둘 다 null이다.
    private readonly ManagementController _management;
    private readonly IReadOnlyList<ResourceCost> _paid;

    /// 지금 어느 단계인가. 파생은 읽기만 한다(전이는 이 클래스의 몫).
    protected State CurrentState { get; private set; } = State.Pending;

    public bool IsConfirmed => CurrentState == State.Confirmed;

    protected ReversibleCommandBase(ManagementController management, IReadOnlyList<ResourceCost> paid)
    {
        _management = management;
        _paid = paid;
    }

    public bool Execute()
    {
        if (CurrentState != State.Pending) return false;

        // 실패하면 Pending에 머문다 — "실패 시 아무것도 바뀌지 않는다"(`IReversibleCommand`) 계약을
        // 상태 축에서 지키는 자리다. 부작용을 남기지 않을 책임은 훅 안에 있다.
        if (!OnExecute()) return false;

        CurrentState = State.Executed;
        return true;
    }

    public void Confirm()
    {
        if (CurrentState != State.Executed) return;
        CurrentState = State.Confirmed;
        OnConfirm();
    }

    public void Commit()
    {
        // ⚠ `Executed`(진행 중 세션)는 확정하지 않는다 — 그건 취소가 `Undo`로 끝낸다(`IReversibleCommand` 주석).
        if (CurrentState != State.Confirmed) return;
        CurrentState = State.Committed;
        OnCommit();
    }

    public void Undo()
    {
        if (CurrentState != State.Executed && CurrentState != State.Confirmed) return;

        bool wasConfirmed = CurrentState == State.Confirmed;
        CurrentState = State.Undone;

        // 환원은 상태 복원이 **성공했을 때만** 한다. 실패한 채로 돌려주면 상태는 그대로인데 자원만
        // 돌아와 무료 업그레이드가 된다(WL-057 무료 배치와 같은 실패 모드).
        if (OnUndo(wasConfirmed) && _management != null) _management.Grant(_paid);
    }

    /// 조작을 실행하거나(합성) 이미 끝난 조작을 인수한다(배치·경영). false면 커맨드는 `Pending`에 머문다.
    protected abstract bool OnExecute();

    /// 세션이 성공으로 끝났다. 상태 전이 말고 할 일이 없으면 비워 둔다.
    protected virtual void OnConfirm() { }

    /// 되돌리기 불가로 확정됐다. 소프트 소모해 둔 것이 있으면 여기서 **진짜로** 파괴한다.
    protected virtual void OnCommit() { }

    /// 조작을 되돌린다. <paramref name="wasConfirmed"/>는 되돌리기 직전 단계가 `Confirmed`였는가다 —
    /// 진행 중 세션의 취소(`Executed`)와 히스토리 되돌리기(`Confirmed`)가 하는 일이 다른 커맨드가 이걸 본다.
    /// 반환값은 **비용을 환원해도 되는가**(= 상태 복원에 성공했는가)다.
    protected abstract bool OnUndo(bool wasConfirmed);
}
