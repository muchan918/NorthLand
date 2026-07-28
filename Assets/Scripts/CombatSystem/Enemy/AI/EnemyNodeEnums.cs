using Unity.Behavior;

// 보스 BT 리프 노드 공용 열거형(#234).
//
// [BlackboardEnum]을 붙이면 그래프 Blackboard의 변수 타입 목록에도 노출된다
// (Unity.Behavior BlackboardRegistry가 이 어트리뷰트로 enum을 수집한다).
// 노드 입력 필드로만 쓸 거면 어트리뷰트 없이도 동작하지만, 설계 원칙이
// "수치는 노드 입력에 인라인으로 박지 말고 Blackboard 변수로 올린다"이므로 붙여둔다.
//
// 네임스페이스를 두지 않는다 — 노드와 같은 규약이므로 이름이 전역에서 유일해야 한다.

// 반경 질의의 대상 종류.
[BlackboardEnum]
public enum EnemyUnitFilter
{
    // 자신과 같은 진영(보스 기준 = 잡몹). 자기 자신은 항상 제외된다.
    Ally,

    // 배치된 타워. Tower.Active 정적 리스트를 순회하므로 물리 질의가 필요 없다.
    // AuraTower는 이 리스트에 등록되지 않아 잡히지 않는다 — 결과적으로 마력 봉인 중에도
    // 오라 계열(감속 포함 가능)은 살아남는다.
    Tower,

    // 자신과 다른 진영(플레이어 유닛·본진).
    Hostile,
}

// 진행 방향 기준 앞뒤 판정. 내적 부호로 가른다.
[BlackboardEnum]
public enum EnemyRelativeDirection
{
    Any,
    Forward,
    Backward,
}

// EnemyResolveTargetAction이 찾아낼 대상의 종류.
[BlackboardEnum]
public enum EnemyTargetKind
{
    // 본진. 밤에 런타임 스폰되므로 초반에는 없는 것이 정상이다.
    PlayerBase,

    NearestTower,

    NearestAlly,

    // 자기 자신. 연출 범위를 자기 위치에 걸 때 쓴다.
    Self,
}
