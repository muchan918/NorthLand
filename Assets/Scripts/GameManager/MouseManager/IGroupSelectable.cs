/// 여러 개를 함께 선택(그룹 선택)할 수 있는 대상이 구현한다. ISelectable(단일 클릭 선택)과 병렬 개념 —
/// 한 오브젝트가 둘 다 구현할 수 있다. MouseManager는 이 마커의 유무로만 "그룹 선택 참여 가능" 여부를
/// 판정하고, 구체 타입(타워 등)은 **전혀 모른다**(입력 단일 창구·제네릭 유지 — SystemMap §6).
///
/// 도메인 해석(이 마커가 어떤 타워인가)은 소비처인 TowerMergeCoordinator가 구현체로 캐스팅해 처리한다
/// — 그래서 이 인터페이스는 도메인(Combat.Tower)을 참조하지 않는다. 향후 병사·건물 등 비-타워 그룹 선택에도
/// 그대로 재사용 가능. 실제 그룹 집합의 소유·집계·게이팅은 TowerMergeCoordinator가 담당한다(Docs/Core/TowerMerge.md §7).
public interface IGroupSelectable
{
    /// 그룹 집합에 추가됐을 때 호출(하이라이트 등 연출). 단일 선택 훅(ISelectable.OnSelected)과 별개다.
    void OnGroupSelected();

    /// 그룹 집합에서 빠졌을 때 호출(하이라이트 해제). 단일 선택 훅(ISelectable.OnDeselected)과 별개다.
    void OnGroupDeselected();
}
