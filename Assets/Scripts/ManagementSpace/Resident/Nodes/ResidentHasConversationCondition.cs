using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 대화 세션을 들고 있으면 참(#276, R3·R4).
//
// **용도가 하나다: Priority Abort의 감시 조건.** 대화 브랜치를 감싼 `Priority Abort`가 이 조건을 매 틱
// 평가하다가 참이 되면, 산책 중이던 낮은 우선순위 브랜치를 즉시 중단시키고 Selector를 처음부터
// 재평가시킨다. 그래서 말을 걸린 쪽이 다음 이동 구간을 기다리지 않고 그 프레임에 합류한다.
//
// 브랜치의 **진입 게이트로는 쓰이지 않는다** — 그 역할은 ResidentConverseAction이 세션 없을 때
// Failure를 반환하는 것으로 이미 성립한다. 조건을 게이트로도 쓰면 같은 판정이 두 곳에 생긴다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[Condition(
    name: "Resident Has Conversation",
    description: "대화 세션에 참가 중이면 참. Priority Abort의 감시 조건으로 쓴다.",
    story: "[Agent] has a conversation",
    category: "Conditions/Resident",
    id: "dd1c042c657a48ab8fa1d294e115150a")]
public partial class ResidentHasConversationCondition : Condition
{
    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    public override bool IsTrue()
    {
        ResidentAgent agent = Agent?.Value;

        // 조용히 거짓으로 둔다. 조건 노드는 매 틱 평가되므로 로그를 남기면 콘솔이 잠긴다 —
        // Agent 링크가 끊긴 경우는 빌더의 자기검사가 잡는다(ResidentBehaviorGraphBuilder).
        return agent != null && agent.HasConversation;
    }
}
