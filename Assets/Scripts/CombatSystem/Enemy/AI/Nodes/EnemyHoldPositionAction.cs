using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 이동 소유권을 잡고 지정 시간 동안 제자리에 멈춘다(#234). P1 돌진의 준비 구간(예고).
//
// 소유권이 필요한 이유: Enemy.Update가 매 프레임 IsStopped를 덮어쓰므로 소유권 없이 정지시키면
// 1프레임 만에 무효화된다. 소유권을 잡으면 Enemy가 이동·타겟 통지에서 손을 뗀다.
//
// 종료 시 정지를 풀고 소유권을 반납한다(중단 포함) — 반납하지 않으면 보스가 영구히 근접 공격을
// 잃고, 정지를 풀지 않으면 소유권 반납 직후 Enemy.Update가 다시 계산해 주지만 한 프레임 어긋난다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Hold Position",
    description: "이동 소유권을 잡고 지정 시간 동안 제자리에 멈춘다. 종료 시 소유권을 반납한다.",
    story: "[Agent] holds position for [Duration] seconds",
    category: "Action/Enemy",
    id: "eff69fcfee36484b8e36ca4dea573c99")]
public partial class EnemyHoldPositionAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    [SerializeReference] public BlackboardVariable<float> Duration;

    private EnemyAgent agent;
    private bool holding;
    private float elapsed;

    protected override Status OnStart()
    {
        agent = Agent?.Value;
        holding = false;

        if (agent == null)
        {
            LogFailure("Enemy Hold Position: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        agent.MovementOwned = true;
        agent.MovementStopped = true;
        holding = true;
        elapsed = 0f;

        // Duration이 0 이하여도 최소 1틱은 Running으로 두지 않는다 — 즉시 종료가 의도라면
        // OnEnd가 곧바로 소유권을 반납해 아무 일도 하지 않은 것과 같아진다.
        float duration = Duration != null ? Duration.Value : 0f;

        return duration <= 0f ? Status.Success : Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null)
        {
            return Status.Failure;
        }

        // 소유권 중에도 다른 노드나 게임 종료가 정지를 건드릴 수 있으므로 매 틱 다시 지시한다.
        agent.MovementStopped = true;

        elapsed += Time.deltaTime;

        float duration = Duration != null ? Duration.Value : 0f;

        return elapsed >= duration ? Status.Success : Status.Running;
    }

    protected override void OnEnd()
    {
        if (holding && agent != null)
        {
            agent.MovementStopped = false;
            agent.MovementOwned = false;
        }

        holding = false;
        agent = null;
    }
}
