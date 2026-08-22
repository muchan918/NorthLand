using System;
using System.Collections.Generic;
using UnityEngine;

/// 자원을 소모하고 **상태 값 하나를 올린** 조작의 범용 되돌리기 커맨드(#444).
///
/// 타워 배치·합성처럼 씬에 실체를 세우거나 없애는 조작은 각자 커맨드를 갖는다 — 파괴·복원의 **순서**가
/// 조작마다 다르기 때문이다(타일 점유를 언제 푸는가, 연출을 어디에 거는가). 반면 경영 조작은 전부 같은
/// 모양이다: **비용을 냈고, 어떤 수치가 한 단계 올라갔다.** 그래서 조작마다 클래스를 만들지 않고
/// "이전 값으로 되돌리는 방법" 하나만 받는다.
///
/// 되돌리는 방법으로 **세이브 복원 API(`ManagementController.TryRestore*`)를 그대로 재사용한다.**
/// 되돌리기 전용 감소 API를 새로 열지 않는 이유: 그 API가 이미 "비용·페이즈 게이트를 타지 않고 상태를
/// 그 값으로 맞춘다"는, 되돌리기에 필요한 것과 정확히 같은 일을 한다. 따로 만들면 같은 런타임 상태를
/// 쓰는 경로가 둘이 되고, 그 둘이 어긋나는 순간을 아무도 못 본다.
///
/// ⚠ **LIFO가 유효성 검사를 겸한다**(`IReversibleCommand` 주석의 그 논지가 경영에도 그대로 적용된다).
/// 본진 레벨이 하위 건물의 해금 상한을 정하므로, 본진을 올리고 → 그 덕에 열린 하위 건물을 올린 상태에서
/// 본진만 먼저 되돌리면 하위 건물이 상한을 넘은 레벨로 남는다. LIFO가 하위 건물을 먼저 되돌리게 강제해
/// 이 경로를 원천 차단한다.
public sealed class ResourceSpendCommand : ReversibleCommandBase
{
    private readonly Func<bool> _revert;
    private readonly string _label; // 로그용 사람이 읽는 이름("나무꾼의 집 업그레이드")

    public ResourceSpendCommand(ManagementController management, IReadOnlyList<ResourceCost> paid,
        Func<bool> revert, string label)
        : base(management, paid)
    {
        _revert = revert;
        _label = string.IsNullOrEmpty(label) ? "자원 소모 조작" : label;
    }

    /// 조작은 **이미 끝나 있다** — 인수(adopt)만 한다(`TowerPlaceCommand`와 같은 계보·같은 이유).
    /// 되돌릴 방법이 없으면 인수하지 않는다: 히스토리에 올려도 눌러도 아무 일이 없는 슬롯 하나가 자리를
    /// 차지하고, 그 위의 진짜 조작이 한 칸 밀려 되돌려진다(`TowerPlacer`의 같은 판단).
    protected override bool OnExecute() => _revert != null;

    protected override bool OnUndo(bool wasConfirmed)
    {
        if (_revert())
        {
            Debug.Log($"[되돌리기] {_label}");
            return true;
        }

        // 상태가 그대로면 비용도 돌려주지 않는다(기반 클래스가 반환값을 그렇게 쓴다) — 여기서 true를 내면
        // 레벨은 유지된 채 자원만 돌아와 무료 업그레이드가 된다.
        Debug.LogWarning($"[되돌리기] {_label}: 상태 복원에 실패해 비용도 환원하지 않았습니다.");
        return false;
    }
}
