using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 받는 피해 배수를 설정한다(#234). P2 방어 태세의 피해 감소.
//
// EnemySetSpeedFactorAction과 같은 형태다: Duration > 0이면 유지 후 원복, 0 이하면 즉시 성공하되
// 원복하지 않는다. 중단 시에도 OnEnd가 원복하므로 감소 배수가 고착되지 않는다 —
// 고착되면 보스가 영구히 단단해져 처치가 불가능해진다.
//
// 배수 0은 무적이다. 방어 태세는 감소치로 쓰되 0을 넣지 않도록 그래프에서 관리한다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Set Damage Taken Factor",
    description: "받는 피해 배수를 설정한다. Duration이 0보다 크면 그 시간 뒤 원복한다.",
    story: "[Agent] sets damage taken factor to [Factor] for [Duration] seconds",
    category: "Action/Enemy",
    id: "c72ab662e5f141688c616fa21bc10e66")]
public partial class EnemySetDamageTakenFactorAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    // 1=그대로, 0.5=절반만 받음, 0=무적.
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
            LogFailure("Enemy Set Damage Taken Factor: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        float factor = Factor != null ? Factor.Value : 1f;
        float duration = Duration != null ? Duration.Value : 0f;

        previousFactor = agent.DamageTakenFactor;
        agent.DamageTakenFactor = factor;

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

    protected override void OnEnd()
    {
        if (shouldRestore && agent != null)
        {
            agent.DamageTakenFactor = previousFactor;
        }

        shouldRestore = false;
        agent = null;
    }
}
