using NorthLand.Combat;

/// 여러 개를 함께 선택(그룹 선택)할 수 있는 대상이 구현한다. ISelectable(단일 클릭 선택)과 병렬 개념 —
/// 한 오브젝트가 둘 다 구현할 수 있다. MouseManager는 이 마커의 유무로만 "그룹 선택 참여 가능" 여부를
/// 판정하고 구체 타입(타워 등)은 모른다(입력 단일 창구·제네릭 유지 — SystemMap §6). 실제 그룹 집합의
/// 소유·집계·게이팅은 TowerMergeCoordinator가 담당한다(Docs/Core/TowerMerge.md §7).
public interface IGroupSelectable
{
    /// 이 대상의 원본 타워. 코디네이터가 재료 식별(Asset.TowerID)과 집합 관리에 쓴다.
    Tower Tower { get; }

    /// 그룹 집합에 추가됐을 때 호출(하이라이트 등 연출). 단일 선택 훅(ISelectable.OnSelected)과 별개다.
    void OnGroupSelected();

    /// 그룹 집합에서 빠졌을 때 호출(하이라이트 해제). 단일 선택 훅(ISelectable.OnDeselected)과 별개다.
    void OnGroupDeselected();
}
