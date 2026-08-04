using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// Destination까지 걸어간다. 도착할 때까지 Running을 유지한다(#276, R2 산책 · 이후 R8 귀가 · R9 등장에서 재사용).
//
// 이동 자체는 NavMeshAgent가 소유하고 이 노드는 목적지만 지시한다. 위치를 직접 쓰지 않는다 —
// 그러면 Agent의 회피·경로 추종과 소유권이 둘로 갈린다(Docs/ManagementArea/Resident.md §8.2의 판단과 같은 계열).
//
// MaxSeconds는 안전장치다. 도달 불가능한 지점이 잡히면(동적 장애물로 경로가 막히는 등)
// 도착 판정이 영원히 서지 않아 브랜치가 멎는다 — EnemyPlayAnimationAction의 MaxWaitSeconds와 같은 이유다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Resident Move To",
    description: "Destination까지 걸어간다. 도착하면 Success. MaxSeconds를 넘기면 중단하고 Success.",
    story: "[Agent] moves to [Destination]",
    category: "Action/Resident",
    id: "7709fc53a2984c86aaa53a8b538867e2")]
public partial class ResidentMoveToAction : Action
{
    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    [SerializeReference] public BlackboardVariable<Vector3> Destination;

    // 목적지 보유 여부. 도착(또는 상한 초과)하면 여기서 내린다 —
    // 그래야 다음 주기에 뽑기 노드가 새 목적지를 받아 온다.
    [SerializeReference] public BlackboardVariable<bool> HasDestination;

    // 이동 상한(초). 0 이하면 상한 없음.
    [SerializeReference] public BlackboardVariable<float> MaxSeconds;

    private ResidentAgent agent;
    private float elapsed;

    protected override Status OnStart()
    {
        agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Resident Move To: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        // NavMesh 밖이면 목적지 지정이 통째로 무시된다. 여기서 걸러야 "성공했는데 안 움직인다"가 안 생긴다.
        if (!agent.IsOnNavMesh)
        {
            LogFailure($"Resident Move To: [{agent.name}]이 NavMesh 위에 있지 않습니다. " +
                "스폰 위치가 베이크된 영역 안인지 확인하세요.");
            return Status.Failure;
        }

        Vector3 destination = Destination != null ? Destination.Value : agent.Position;

        if (!agent.TrySetDestination(destination))
        {
            LogFailure($"Resident Move To: [{agent.name}]의 목적지 {destination} 설정에 실패했습니다.");
            return Status.Failure;
        }

        elapsed = 0f;
        agent.SetMoving(true);

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null)
        {
            return Status.Failure;
        }

        elapsed += Time.deltaTime;

        float maxSeconds = MaxSeconds != null ? MaxSeconds.Value : 0f;

        if (maxSeconds > 0f && elapsed >= maxSeconds)
        {
            // 실패가 아니라 성공으로 돌린다. 도달 못 한 것은 사실이지만 산책에 실패란 없고,
            // Failure로 돌리면 셀렉터가 상위 브랜치로 튀어 유휴조차 돌지 않는다.
            // 목적지는 여기서도 내린다 — 안 내리면 도달 불가능한 지점을 영원히 다시 시도한다.
            ClearDestination();
            return Status.Success;
        }

        if (!agent.HasArrived)
        {
            return Status.Running;
        }

        ClearDestination();
        return Status.Success;
    }

    // 도착했으니 목적지를 비운다. 다음 주기에 뽑기 노드가 새 목적지를 받아 온다.
    private void ClearDestination()
    {
        if (HasDestination != null)
        {
            HasDestination.Value = false;
        }
    }

    protected override void OnEnd()
    {
        if (agent != null)
        {
            // 중단(밤 전환·드래그)으로 끝났을 수도 있다. 경로를 남겨 두면 다음 브랜치가
            // 정지를 지시해도 Agent가 계속 끌고 간다.
            agent.StopMoving();
            agent.SetMoving(false);
        }

        agent = null;
    }
}
