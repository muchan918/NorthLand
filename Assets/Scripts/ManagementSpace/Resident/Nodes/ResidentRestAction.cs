using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 걷는 도중 확률적으로 잠깐 멈춘다(#276, R15 휴식).
//
// 왜 필요한가: 웨이포인트가 멀면 주민이 도착까지 한 번도 안 멈추고 행군한다. 사람이 걷는 그림이
// 아니라 컨베이어에 실린 그림이 된다.
//
// **목적지를 건드리지 않는다.** NavMeshAgent를 정지시키되 경로는 남겨 두므로(ResidentAgent.PauseMovement),
// 재개하면 가던 길을 그대로 이어 간다. ResidentAgent.StopMoving은 ResetPath까지 하므로 여기 쓸 수 없다.
//
// ── 언제 쉬는가 ────────────────────────────────────────────────
//
// 이동 노드가 구간(SegmentSeconds)마다 브랜치를 끊고 이 노드로 넘어오므로, **구간마다 한 번**
// 판정이 돈다. 그래서 별도 타이머 없이 "여정이 길수록 판정이 잦다"가 성립한다 —
// 먼 웨이포인트일수록 자연히 여러 번 쉬고, 가까우면 거의 안 쉰다.
//
// 시간·거리 고정 주기로 하지 않는 이유: 30명이 같은 웨이포인트로 향할 때 다 같이 같은 박자로
// 멈춰 군무가 된다. 확률로 굴려야 개체마다 어긋난다.
//
// 도착 임박에는 쉬지 않는다(MinRemainingDistance). 목적지 몇 걸음 앞에서 멈추면 길을 잃은 것처럼 보인다.
// 출발 직후는 첫 판정이 구간 끝에 오므로 저절로 막힌다 — 따로 조건을 두지 않는다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Resident Rest",
    description: "걷는 도중 확률적으로 잠깐 멈춘다. 목적지는 그대로 둔다. 쉬지 않으면 즉시 통과한다.",
    story: "[Agent] may rest briefly",
    category: "Action/Resident",
    id: "50ce941d5ce343a4ac3bf83680c494ef")]
public partial class ResidentRestAction : Action
{
    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    // 한 번 판정할 때 쉴 확률(0~1).
    [SerializeReference] public BlackboardVariable<float> Chance;

    // 남은 거리가 이보다 짧으면 쉬지 않는다.
    [SerializeReference] public BlackboardVariable<float> MinRemainingDistance;

    // 휴식 길이의 하한·상한(초). 고정값이면 또 박자가 맞아떨어지므로 구간에서 뽑는다.
    [SerializeReference] public BlackboardVariable<float> MinSeconds;

    [SerializeReference] public BlackboardVariable<float> MaxSeconds;

    private ResidentAgent agent;
    private float remaining;

    protected override Status OnStart()
    {
        agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Resident Rest: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        // 쉬지 않기로 했으면 즉시 통과한다 — 이 노드는 "가끔 끼어드는" 것이지 매번 도는 것이 아니다.
        if (!ShouldRest())
        {
            agent = null;
            return Status.Success;
        }

        float min = MinSeconds != null ? MinSeconds.Value : 0f;
        float max = MaxSeconds != null ? MaxSeconds.Value : min;
        remaining = max > min ? Random.Range(min, max) : min;

        if (remaining <= 0f)
        {
            agent = null;
            return Status.Success;
        }

        agent.PauseMovement();
        agent.SetMoving(false);

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null)
        {
            return Status.Failure;
        }

        remaining -= Time.deltaTime;

        return remaining > 0f ? Status.Running : Status.Success;
    }

    protected override void OnEnd()
    {
        // 중단으로 끝났을 수도 있다. 재개를 여기서 보장하지 않으면 주민이 멈춘 채 굳는다.
        if (agent != null)
        {
            agent.ResumeMovement();
            agent.SetMoving(true);
        }

        agent = null;
    }

    private bool ShouldRest()
    {
        // 대화가 성립된 주민은 이미 그 자리에 세워져 있다(ResidentTryStartConversationAction).
        // 여기서 쉬기 시작하면 OnEnd의 ResumeMovement가 그 정지를 풀어, 대화 상대를 두고 걸어가 버린다.
        //
        // 보통은 Priority Abort가 이 시퀀스를 그 전에 중단시켜 여기까지 오지 않는다. 선점이 어떤 이유로든
        // 동작하지 않을 때의 안전망이다 — 이 가드가 있으면 선점 실패가 버그가 아니라 합류 지연으로 끝난다.
        if (agent.HasConversation)
        {
            return false;
        }

        float chance = Chance != null ? Chance.Value : 0f;

        if (chance <= 0f || Random.value >= chance)
        {
            return false;
        }

        float minRemaining = MinRemainingDistance != null ? MinRemainingDistance.Value : 0f;

        // 경로 계산 중이면 RemainingDistance가 무한대라 이 조건을 통과한다 — 곧 값이 잡히므로
        // 다음 구간에 정상 판정된다. 계산 전 0을 읽고 "도착 임박"으로 오판하는 것보다 안전하다.
        return agent.RemainingDistance >= minRemaining;
    }
}
