using System.Collections.Generic;
using UnityEngine;

/// 타워 배치를 되돌릴 수 있게 감싼 커맨드(#281). `TowerPlacer.PlaceTower`가 배치를 마친 뒤 만든다.
///
/// **배치를 수행하지 않고 인수(adopt)한다.** 합성 커맨드가 "재료를 소모한다"를 직접 실행하는 것과
/// 대비되는데, 이유는 배치 본문이 `TowerPlacer`의 프레임 캐시(`_footprint`)·프리팹 필드·타일 버프
/// 계산기에 붙어 있어 커맨드로 옮기면 Placer의 절반을 인자로 끌고 가야 하기 때문이다. 게다가 지금
/// 배치 검증 실패는 전부 `return`이라 **부분 실패 롤백 경로가 아예 없는데**, 옮기면
/// `Instantiate` 뒤 실패했을 때의 롤백을 새로 써야 한다 — 없던 실패 모드를 만드는 셈이다.
/// 인수 방식에서도 "Execute 실패 시 아무것도 바뀌지 않는다" 계약은 성립한다: 실패하면 커맨드가
/// `Pending`에 머물 뿐이고, 부작용은 "이 배치는 되돌릴 수 없다" 하나다.
///
/// 합성 결과 타워도 이 커맨드로 만들어지지만 히스토리에 오르지 않는다 —
/// `TowerMergeCommand.AdoptResult`가 편입해 합성 전체가 **한 번에** 되돌아가게 한다.
public class TowerPlaceCommand : IReversibleCommand
{
    private enum State { Pending, Executed, Confirmed, Committed, Undone }

    private readonly GameObject _placed;
    private readonly IReadOnlyList<ResourceCost> _paid; // 실제로 차감된 비용. 무료 배치면 null
    private readonly ManagementController _management;  // null이면 무료 씬 — 환원할 것도 없다
    private readonly float _tileSize;                   // 되돌리기 연출의 모든 길이 기준(한 칸)
    private State _state = State.Pending;

    public bool IsConfirmed => _state == State.Confirmed;

    /// 되돌릴 때 소멸 연출을 이 커맨드가 **직접** 재생하는가. 합성이 결과로 편입하면 false로 내린다 —
    /// 합성 되돌리기는 "가루가 재료 자리로 돌아간다"는 하나의 연출(Rewind)이라, 결과 타워가 자기 몫으로
    /// 한 번 더 터지면 같은 자리에서 두 연출이 겹친다.
    /// **연출의 주인은 항상 히스토리에 올라간 바깥쪽 커맨드다.**
    public bool PlaysUndoDissolve { get; set; } = true;

    /// 배치된 타워. `TowerPlacer`의 확정 콜백이 예전에 `Transform`을 넘기던 계약을 그대로 잇는다 —
    /// 합성 연출이 수렴 목적지를 재는 데 쓰고, **등장 연출이 스케일을 0으로 만들기 전에** 읽어야 한다.
    public Transform Placed => _placed != null ? _placed.transform : null;

    public TowerPlaceCommand(GameObject placed, IReadOnlyList<ResourceCost> paid,
        ManagementController management, float tileSize)
    {
        _placed = placed;
        _paid = paid;
        _management = management;
        _tileSize = tileSize;
    }

    /// 배치 결과를 인수한다. **부작용이 없다** — 배치는 이미 끝나 있다.
    public bool Execute()
    {
        if (_state != State.Pending || _placed == null) return false;
        _state = State.Executed;
        return true;
    }

    public void Confirm()
    {
        if (_state != State.Executed) return;
        _state = State.Confirmed;
    }

    /// ⚠ **파괴할 소프트 소모물이 없다 — 상태 전이만 한다.** 배치는 합성과 달리 "임시로 치워둔 것"이
    /// 없기 때문이다. 여기서 의미가 있는 것은 오직 "이 뒤로 Undo가 무시된다"이다.
    public void Commit()
    {
        if (_state != State.Confirmed) return;
        _state = State.Committed;
    }

    public void Undo()
    {
        if (_state != State.Executed && _state != State.Confirmed) return;
        _state = State.Undone;

        if (_placed != null)
        {
            // 연출은 파괴 **전에** 건다 — 복제할 시각물이 남아 있어야 한다(TowerDissolveEffect 계약).
            // Play의 Build가 동기로 끝나므로 바로 아래에서 Destroy해도 안전하다.
            if (PlaysUndoDissolve)
            {
                NorthLand.Combat.TowerDissolveEffect.Play(
                    new[] { _placed.transform },
                    _tileSize,
                    NorthLand.Combat.DissolveMode.Disperse);
            }

            // ⚠ `Destroy`는 프레임 끝까지 지연되므로 `TowerFootprint.OnDestroy`도 그때까지 안 돈다.
            //    점유는 **지금** 명시적으로 푼다 — 합성 되돌리기가 곧바로 재료를 `Reoccupy`할 때
            //    안 풀려 있으면 `TowerFootprint.Reoccupy`가 "이미 점유돼 있습니다" 경고를 내며
            //    그 타일을 목록에서 빼버려 **재료가 타일 없는 타워로 되살아난다.**
            //    Release 뒤에는 OnDestroy가 조기 return하므로 이중 해제도 없다.
            if (_placed.TryGetComponent(out TowerFootprint footprint)) footprint.Release();
            Object.Destroy(_placed);
        }

        // 지불한 만큼 100% 환원한다. 커맨드가 **실지불 비용**을 들고 있으므로 합성 결과 타워도
        // ExtraCost만 정확히 돌아간다(재료 원가는 재료가 되살아나는 것으로 갚아진다).
        if (_management != null) _management.Grant(_paid);
    }
}
