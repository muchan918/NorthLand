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
///
/// #281로 실제 파괴 시점이 **배치 확정 → 밤 진입**으로 밀렸다. 그 사이 구간(`Confirmed`)에서는
/// 재료가 비활성인 채 되살아날 수 있는 상태로 대기한다 — 되돌리기가 노리는 창이 정확히 이 구간이다.
public class TowerMergeCommand : IReversibleCommand
{
    private enum State { Pending, Executed, Confirmed, Committed, Undone }

    private readonly List<Tower> _materials;
    private readonly float _tileSize; // 되돌리기 연출(Rewind)의 모든 길이 기준(한 칸)
    private State _state = State.Pending;

    // 합성 결과 타워의 배치 커맨드. 확정 콜백에서 편입된다(AdoptResult).
    // 되돌리기가 "재료 복원"만이 아니라 "결과 회수 + 재료 복원"이려면 결과를 붙들고 있어야 한다.
    private TowerPlaceCommand _result;

    public bool IsConfirmed => _state == State.Confirmed;

    public TowerMergeCommand(List<Tower> materials, float tileSize)
    {
        _materials = materials;
        _tileSize = tileSize;
    }

    public bool Execute()
    {
        if (_state != State.Pending || _materials == null || _materials.Count == 0) return false;

        int consumed = 0;
        foreach (Tower tower in _materials)
        {
            if (tower == null) continue;

            // 타일을 먼저 푼다 — 자리를 비우는 것이 이 커맨드의 존재 이유다.
            if (tower.TryGetComponent(out TowerFootprint footprint)) footprint.Release();

            // OnDisable 연쇄: 행동 Dispose(버프 오라가 남의 타워에 건 modifier 회수) → Unregister
            // (Tower.Active에서 빠짐 → 코디네이터의 Prune이 선택 집합에서도 제거) → stats.Clear().
            tower.gameObject.SetActive(false);
            consumed++;
        }

        // 하나도 못 소모했으면 바뀐 것이 없다 → 계약대로 Pending을 유지하고 false를 낸다.
        // 이걸 true로 흘리면 호출부가 배치를 시작해 **재료 없이 결과 타워가 선다**(공짜 합성).
        if (consumed == 0)
        {
            Debug.LogWarning("[TowerMergeCommand] 소모할 수 있는 재료가 하나도 없습니다.");
            return false;
        }

        // 일부만 소모된 경우는 되돌리지 않고 진행한다. 사라진 재료는 되돌릴 것이 없고, 여기서 false를
        // 내면 **이미 소모한 쪽이 복구되지 않은 채 남는다**("실패 시 아무것도 바뀌지 않는다" 계약 위반).
        if (consumed != _materials.Count)
        {
            Debug.LogWarning(
                $"[TowerMergeCommand] 재료 {_materials.Count}개 중 {consumed}개만 소모했습니다 — " +
                "나머지는 이미 사라진 상태입니다.");
        }

        _state = State.Executed;
        return true;
    }

    /// 결과 타워의 배치 커맨드를 이 합성의 일부로 편입한다(#281). `Confirm` 직전에 부른다.
    ///
    /// 편입한 뒤로 결과 배치는 **히스토리에 따로 오르지 않는다**(`PlacementOwner.Caller`가 그것을 막는다).
    /// 한 번의 합성이 "결과 회수"와 "재료 복원" 두 번에 나눠 되감기면, 중간 상태(결과도 재료도 없는
    /// 빈 타일)가 사용자에게 한 번 보인다.
    public void AdoptResult(TowerPlaceCommand result)
    {
        _result = result;

        // 연출 소유권도 함께 넘어온다 — 되돌리기는 Rewind 하나로 그려지고, 결과 타워가 자기 몫으로
        // 한 번 더 터지면 같은 자리에서 두 연출이 겹친다.
        if (_result != null) _result.PlaysUndoDissolve = false;
    }

    /// 배치가 확정됐다. **재료를 파괴하지 않는다** — 밤까지는 되돌릴 수 있어야 하기 때문이다(#281).
    public void Confirm()
    {
        if (_state != State.Executed) return;
        _state = State.Confirmed;
        _result?.Confirm();
    }

    public void Commit()
    {
        // ⚠ `Executed`(배치 진행 중)에서는 확정하지 않는다. 밤 진입 시 진행 중인 세션은
        //    `PhasePanelSwitcher.ShowNight`의 배치 취소가 `Undo`로 끝내므로, 여기서 먼저 파괴하면
        //    그 취소가 되살릴 재료를 잃는다. 확정 대상은 `Confirm`을 지난 것뿐이다.
        if (_state != State.Confirmed) return;
        _state = State.Committed;
        _result?.Commit();

        foreach (Tower tower in _materials)
        {
            if (tower == null) continue;
            Object.Destroy(tower.gameObject);
        }
    }

    public void Undo()
    {
        // `Executed` = 배치 세션이 취소로 끝났다(#263의 원래 경로). 결과 타워는 아직 없으므로
        // 재료만 되살리면 끝이다.
        if (_state == State.Executed)
        {
            _state = State.Undone;
            RestoreMaterials();
            return;
        }

        // `Confirmed` = 되돌리기 버튼(#281). 결과를 회수하고 재료를 되살린다.
        if (_state != State.Confirmed) return;
        _state = State.Undone;

        // ① 연출을 결과 타워가 **아직 살아 있는 동안** 건다 — Play가 시각 사본을 동기로 뜬다.
        TowerDissolveEffect effect = TowerDissolveEffect.Play(
            new[] { _result?.Placed },
            _tileSize,
            DissolveMode.Rewind);

        // ② 결과 회수가 **반드시 재료 복원보다 먼저**다. 결과 타워의 풋프린트는 재료가 쓰던 타일과
        //    겹치는데(그 자리에 놓을 수 있게 한 것이 #263의 목적이다), 그 점유가 풀리기 전에 재료를
        //    Reoccupy하면 `TowerFootprint`가 "이미 점유돼 있습니다"로 그 타일을 포기한다.
        //    _result.Undo()가 Destroy 전에 Release를 부르는 것이 이 순서를 성립시킨다.
        _result?.Undo();

        // ③ 재료를 되살린다.
        RestoreMaterials();

        // ④ 되살아난 재료를 가루의 목적지로 등록하고 **같은 프레임에** 숨긴다 — ③이 SetActive(true)를
        //    걸었으므로 렌더 전에 잡지 않으면 원본 크기 타워가 한 프레임 번쩍인다(Reassemble과 같은 규율).
        effect.RestoreTo(CollectMaterialTransforms());
    }

    private List<Transform> CollectMaterialTransforms()
    {
        var result = new List<Transform>(_materials.Count);
        foreach (Tower tower in _materials)
        {
            if (tower != null) result.Add(tower.transform);
        }
        return result;
    }

    private void RestoreMaterials()
    {
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
