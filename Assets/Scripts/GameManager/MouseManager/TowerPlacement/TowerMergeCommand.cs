using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;

/// 합성 재료 소모를 되돌릴 수 있게 감싼 커맨드(#263).
///
/// 예전에는 **배치 확정 시점에** 재료를 Destroy했다. 그래서 재료가 점유한 타일에는 결과 타워를 놓을 수
/// 없었다 — 재료는 확정 후에야 파괴되어 타일이 풀리기 때문이다(WL-077a). 촘촘히 깐 상태에서 합성하면
/// 놓을 자리가 없다. 이제 후보 버튼을 누르는 즉시 소모해 자리를 비우고, 배치를 취소하면 되돌린다.
///
/// `Destroy`는 되돌릴 수 없으므로 **소프트 소모**로 한다: 타일 점유 해제 + GameObject 비활성화.
/// 나머지(`Tower.Active` 등록 해제, 스탯 원장 비움, 버프 오라가 남긴 modifier 회수, 재활성화 시 복원)는
/// `Tower.OnDisable`/`OnEnable`이 **이미 대칭으로** 처리한다 — 풀 재사용을 대비해 만들어 둔 왕복이
/// 그대로 쓰이므로 이 커맨드가 따로 손댈 것이 없다.
public class TowerMergeCommand : IReversibleCommand
{
    private enum State { Pending, Executed, Committed, Undone }

    private readonly List<Tower> _materials;
    private State _state = State.Pending;

    /// 확정됐는가. 배치 세션 종료 통지(`TowerPlacer`의 OnEnded)는 **확정으로 끝났는지 취소로 끝났는지를
    /// 알려주지 않으므로**, 그 판단을 커맨드가 자기 상태로 한다. 덕분에 `TowerPlacer`/`MouseManager`의
    /// 배치 코어를 건드리지 않고도 "취소일 때만 원복"이 성립한다.
    public bool IsCommitted => _state == State.Committed;

    public TowerMergeCommand(List<Tower> materials)
    {
        _materials = materials;
    }

    public bool Execute()
    {
        if (_state != State.Pending || _materials == null || _materials.Count == 0) return false;

        foreach (Tower tower in _materials)
        {
            if (tower == null) continue;

            // 타일을 먼저 푼다 — 자리를 비우는 것이 이 커맨드의 존재 이유다.
            if (tower.TryGetComponent(out TowerFootprint footprint)) footprint.Release();

            // OnDisable 연쇄: 행동 Dispose(버프 오라가 남의 타워에 건 modifier 회수) → Unregister
            // (Tower.Active에서 빠짐 → 코디네이터의 Prune이 선택 집합에서도 제거) → stats.Clear().
            tower.gameObject.SetActive(false);
        }

        _state = State.Executed;
        return true;
    }

    public void Commit()
    {
        if (_state != State.Executed) return;
        _state = State.Committed;

        foreach (Tower tower in _materials)
        {
            if (tower == null) continue;
            Object.Destroy(tower.gameObject);
        }
    }

    public void Undo()
    {
        if (_state != State.Executed) return;
        _state = State.Undone;

        foreach (Tower tower in _materials)
        {
            if (tower == null) continue;

            // 점유를 먼저 되돌리고 살린다. 되살아난 타워가 발행하는 ActiveChanged를 구독자가 받는 시점에는
            // 타일 상태까지 이미 제자리여야 한다 — 지금 구독자 중엔 타일을 읽는 쪽이 없지만, 순서에
            // 기대는 구독자가 나중에 붙어도 깨지지 않게 해 둔다.
            if (tower.TryGetComponent(out TowerFootprint footprint)) footprint.Reoccupy();

            // OnEnable 연쇄: 타일 버프 재적용 → 행동 재무장 → Register. 되살아난 재료는 다시 선택되고
            // 다시 합성 재료가 된다.
            tower.gameObject.SetActive(true);
        }
    }
}
