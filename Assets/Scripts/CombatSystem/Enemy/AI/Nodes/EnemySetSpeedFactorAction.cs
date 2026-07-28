using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 패턴 이동속도 배수를 설정한다(#234). P2 방어 태세의 크롤, 기본 진군의 배수 1 복귀에 쓴다.
//
// Duration > 0이면 그 시간 동안 유지한 뒤 원복하고, 0 이하면 즉시 성공하되 원복하지 않는다
// (기본 진군처럼 "이 값으로 두고 간다"가 필요한 경우).
//
// 중단되어도 상태를 남기지 않는다: Duration > 0로 유지 중 중단되면 OnEnd가 원복한다.
// 원복 대상은 노드 진입 시점의 값이지 1이 아니다 — 상위에서 다른 배수를 걸어둔 경우를 지우지 않도록.
//
// 패턴 배수는 감속 디버프 축과 곱해지므로 이 노드가 감속 타워의 효과를 지우지 않는다.
// 하한 클램프가 걸려 있어 배수를 0으로 내려도 완전히 멈추지는 않는다 — 정지는
// EnemyHoldPositionAction(이동 소유권)의 일이다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Set Speed Factor",
    description: "패턴 이동속도 배수를 설정한다. Duration이 0보다 크면 그 시간 뒤 원복한다.",
    story: "[Agent] sets speed factor to [Factor] for [Duration] seconds",
    category: "Action/Enemy",
    id: "d549ecd111844c1cba7561959b576661")]
public partial class EnemySetSpeedFactorAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    // 1=기본, 2=2배, 0.1=크롤.
    [SerializeReference] public BlackboardVariable<float> Factor;

    // 유지 시간(초). 0 이하면 즉시 성공하고 원복하지 않는다.
    [SerializeReference] public BlackboardVariable<float> Duration;

    private EnemyAgent agent;
    private float previousFactor;
    private bool shouldRestore;
    private float elapsed;

    protected override Status OnStart()
    {
        agent = Agent?.Value;
        shouldRestore = false;

        if (agent == null)
        {
            LogFailure("Enemy Set Speed Factor: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        float factor = Factor != null ? Factor.Value : 1f;
        float duration = Duration != null ? Duration.Value : 0f;

        previousFactor = agent.PatternSpeedFactor;
        agent.PatternSpeedFactor = factor;

        if (duration <= 0f)
        {
            return Status.Success;
        }

        shouldRestore = true;
        elapsed = 0f;

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null)
        {
            return Status.Failure;
        }

        elapsed += Time.deltaTime;

        float duration = Duration != null ? Duration.Value : 0f;

        return elapsed >= duration ? Status.Success : Status.Running;
    }

    // 정상 종료와 상위 컴포지트에 의한 중단 모두 이 경로를 지난다.
    protected override void OnEnd()
    {
        if (shouldRestore && agent != null)
        {
            agent.PatternSpeedFactor = previousFactor;
        }

        shouldRestore = false;
        agent = null;
    }
}
