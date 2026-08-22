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
///
/// **상태 기계와 비용 환원은 `ReversibleCommandBase`가 갖는다**(#444) — 되돌릴 수 있는 조작이
/// 경영까지 셋이 되면서 승격한 자리다. 여기 남은 것은 "타워 배치를 되돌린다는 게 무슨 뜻인가"뿐이다.
public class TowerPlaceCommand : ReversibleCommandBase
{
    private readonly GameObject _placed;
    private readonly float _tileSize; // 되돌리기 연출의 모든 길이 기준(한 칸)

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
        : base(management, paid) // 지불한 만큼 100% 환원하는 것은 기반 클래스가 한다
    {
        _placed = placed;
        _tileSize = tileSize;
    }

    /// 배치 결과를 인수한다. **부작용이 없다** — 배치는 이미 끝나 있다.
    protected override bool OnExecute() => _placed != null;

    /// ⚠ **파괴할 소프트 소모물이 없다.** 배치는 합성과 달리 "임시로 치워둔 것"이 없어 `Commit`에서
    /// 할 일이 없다 — 의미가 있는 것은 오직 "이 뒤로 Undo가 무시된다"이고, 그 판정은 기반 클래스가 한다.
    /// (그래서 `OnCommit`을 재정의하지 않는다.)
    protected override bool OnUndo(bool wasConfirmed)
    {
        // 히스토리 되돌리기(`Confirmed`)에서만 선택을 푼다. 방금 배치한 타워가 선택 중이면 인포 패널·
        // 사거리 원이 파괴된 타워를 붙들고 남는다(WL-086 계열). **진행 중 세션의 취소(`Executed`)에서는
        // 풀지 않는다** — 그 경로는 합성 취소가 타는 길이고, 재료 선택을 함께 날릴 이유가 없다.
        //
        // 이 판단이 요청 진입점(`UndoRequest`)이 아니라 커맨드에 있는 이유: **무엇이 파괴되는지 아는 쪽이
        // 커맨드다.** 진입점에서 무조건 풀면 건물 업그레이드를 되돌릴 때(#444) 방금 올린 건물의 패널이
        // 닫혀 되돌아간 레벨을 볼 수 없다(`ResidentSelectionCoordinator`의 "주민이었을 때만 푼다"와 같은 판단).
        if (wasConfirmed) MouseManager.Instance?.ClearSelection();

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

        // 대상이 이미 사라졌어도 환원은 한다(true) — 플레이어가 낸 값은 조작이 성립했던 순간의 사실이고,
        // 그 뒤에 타워가 어떤 경로로 없어졌는지와 무관하다.
        return true;
    }
}
