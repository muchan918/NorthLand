using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 춤추는 중에 누군가 다가왔으면 참(#276, R5).
//
// **용도가 하나다: Priority Abort의 감시 조건.** 참이 되는 순간 낮은 우선순위 형제인 춤 브랜치가
// 중단되고 Selector가 처음부터 재평가된다 — 대화 합류에 쓴 것과 같은 기계장치다(§11.3).
//
// 춤은 "아무도 없을 때만" 시작하므로(ResidentDanceAction), 이 조건이 참이 된다는 것은
// **춤추는 도중에 사람이 들어왔다**는 뜻이다. 시작 조건과 중단 조건이 같은 축이라 둘이 어긋나지 않는다.
//
// ⚠ IsDancing을 **먼저** 본다. 반경 질의가 주민 수만큼 도는 선형 탐색인데 이 조건은 매 틱 평가되므로,
//   춤추지 않는 대다수의 주민에게서 그 탐색이 돌면 안 된다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[Condition(
    name: "Resident Dance Seen",
    description: "춤추는 중이고 반경 안에 다른 주민이 있으면 참. Priority Abort의 감시 조건으로 쓴다.",
    story: "[Agent] is dancing and someone came near",
    category: "Conditions/Resident",
    id: "4508c9fef7a34c62a903f72c0c078650")]
public partial class ResidentDanceSeenCondition : Condition
{
    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    [SerializeReference] public BlackboardVariable<float> Radius;

    // 이 수를 **넘으면** 목격된 것으로 본다. 0이면 한 명이라도 들어오면 성립한다.
    [SerializeReference] public BlackboardVariable<int> MaxNeighbors;

    public override bool IsTrue()
    {
        ResidentAgent agent = Agent?.Value;
        Resident self = agent != null ? agent.Resident : null;

        // 조용히 거짓으로 둔다. 조건 노드는 매 틱 평가되므로 로그를 남기면 콘솔이 잠긴다 —
        // Agent 링크가 끊긴 경우는 빌더의 자기검사가 잡는다(ResidentBehaviorGraphBuilder).
        if (self == null || !self.IsDancing)
        {
            return false;
        }

        float radius = Radius != null ? Radius.Value : 0f;
        int maxNeighbors = MaxNeighbors != null ? MaxNeighbors.Value : 0;

        return ResidentRegistry.CountNearby(self, radius) > maxNeighbors;
    }
}
