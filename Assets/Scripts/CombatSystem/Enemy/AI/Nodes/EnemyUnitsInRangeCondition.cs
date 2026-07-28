using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 지정 방향 반경 안의 대상 수가 임계값 이상인지 검사한다(#234).
// P2 방어 태세(뒤쪽 잡몹)와 P3 마력 봉인(앞쪽 타워 + 앞쪽 잡몹)의 공용 트리거.
//
// Filter / Direction을 열거형으로 받아 노드 하나로 겸용한다 — 방향별·대상별로 노드를 쪼개면
// 같은 판정 로직이 6벌로 복제된다. Unity.Behavior가 enum Blackboard 변수를 지원하는 것을
// 패키지 소스에서 확인했다(BlackboardRegistry의 BlackboardEnumAttribute 수집).
//
// 레이어 마스크는 노드 입력이 아니라 EnemyAgent.UnitLayerMask에서 읽는다 —
// LayerMask가 Blackboard 변수 지원 타입 목록에 없다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[Condition(
    name: "Enemy Units In Range",
    description: "지정 방향 반경 안의 대상(아군/타워/적군) 수가 임계값 이상이면 참.",
    story: "[Agent] has [MinCount] or more [Filter] within [Radius] to the [Direction]",
    category: "Conditions/Enemy",
    id: "f7ba8bd406854e05a2041581a746a4d0")]
public partial class EnemyUnitsInRangeCondition : Condition
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    [SerializeReference] public BlackboardVariable<EnemyUnitFilter> Filter;

    [SerializeReference] public BlackboardVariable<EnemyRelativeDirection> Direction;

    [SerializeReference] public BlackboardVariable<float> Radius;

    // 이 수 이상이면 참. 0 이하면 항상 참이 되므로 1 이상을 쓴다.
    [SerializeReference] public BlackboardVariable<int> MinCount;

    public override bool IsTrue()
    {
        EnemyAgent agent = Agent?.Value;

        if (agent == null)
        {
            return false;
        }

        int minCount = MinCount != null ? MinCount.Value : 0;

        if (minCount <= 0)
        {
            return true;
        }

        int count = EnemyNodeQuery.CountInRange(
            agent,
            Filter != null ? Filter.Value : EnemyUnitFilter.Ally,
            Direction != null ? Direction.Value : EnemyRelativeDirection.Any,
            Radius != null ? Radius.Value : 0f);

        return count >= minCount;
    }
}
