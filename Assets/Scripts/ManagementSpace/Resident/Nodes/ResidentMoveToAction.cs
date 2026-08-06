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

    // 한 번에 이동할 구간 길이(초). 0 이하면 도착할 때까지 한 번에 간다.
    //
    // 도착 전이라도 이 시간이 지나면 목적지를 **유지한 채** Success를 돌려 브랜치를 끊는다.
    // 두 가지를 얻는다:
    //  · 뒤따르는 휴식 노드(R15)가 걷는 도중에 끼어들 자리가 생긴다
    //  · 브랜치가 짧아져 밤 전환·이탈처럼 즉시 끊겨야 하는 것에 덜 취약해진다
    //    (Docs/ManagementArea/Resident.md §7 「Selector는 비선점이다」)
    [SerializeReference] public BlackboardVariable<float> SegmentSeconds;

    // 이동 상한(초). 0 이하면 상한 없음.
    [SerializeReference] public BlackboardVariable<float> MaxSeconds;

    private ResidentAgent agent;

    // **시계가 둘이다. 하나로 합치면 안 된다.**
    //
    //  · segmentElapsed — 이번 구간에서 걸은 시간. 진입마다 0에서 시작한다(SegmentSeconds 판정용)
    //  · journeyElapsed — 이 목적지를 향해 걸은 **누적** 시간. 구간을 넘어 이어진다(MaxSeconds 판정용)
    //
    // ⚠ 하나로 쓰면 두 판정 중 하나는 반드시 틀린다. 실제로 겪었다 — 누적 시계로 구간을 재면
    //   둘째 구간부터 진입 시점에 이미 SegmentSeconds를 넘겨 **첫 프레임에 Success로 빠져나오고**,
    //   그 틈으로 휴식 노드(PauseMovement)가 매번 끼어들어 "한 발짝 걷고 멈춤"이 반복된다.
    //   반대로 구간 시계로 상한을 재면 4초마다 0으로 돌아가 MaxSeconds가 영원히 발동하지 않는다.
    private float segmentElapsed;
    private float journeyElapsed;

    // 직전 종료가 **구간 끝**이었는가(목적지를 들고 나갔는가). 참이면 journeyElapsed를 잇는다.
    private bool continueJourney;

    protected override Status OnStart()
    {
        agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Resident Move To: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        // NavMesh 밖이면 목적지 지정이 통째로 무시된다. 여기서 걸러야 "성공했는데 안 움직인다"가 안 생긴다.
        // 벗어난 경우 한 번 끌어올려 본다 — 밀려나 오프메시가 된 주민이 영원히 굳는 것을 막는다.
        if (!agent.EnsureOnNavMesh())
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

        // 구간 시계는 **항상** 0에서 시작한다. 여정 시계는 같은 목적지를 이어받은 것일 때만 잇는다.
        segmentElapsed = 0f;

        if (!continueJourney)
        {
            journeyElapsed = 0f;
        }

        continueJourney = false;
        agent.SetMoving(true);

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null)
        {
            return Status.Failure;
        }

        segmentElapsed += Time.deltaTime;
        journeyElapsed += Time.deltaTime;

        float maxSeconds = MaxSeconds != null ? MaxSeconds.Value : 0f;

        if (maxSeconds > 0f && journeyElapsed >= maxSeconds)
        {
            // 실패가 아니라 성공으로 돌린다. 도달 못 한 것은 사실이지만 산책에 실패란 없고,
            // Failure로 돌리면 셀렉터가 상위 브랜치로 튀어 유휴조차 돌지 않는다.
            // 목적지는 여기서도 내린다 — 안 내리면 도달 불가능한 지점을 영원히 다시 시도한다.
            ClearDestination();
            return Status.Success;
        }

        if (agent.HasArrived)
        {
            ClearDestination();
            return Status.Success;
        }

        // 구간 끝 — 목적지는 그대로 두고 브랜치만 끊는다. 다음 주기에 뽑기 노드가 통과시키고
        // 이 노드가 다시 이어받는다. 걷는 것은 멈추지 않는다(OnEnd 참조).
        float segmentSeconds = SegmentSeconds != null ? SegmentSeconds.Value : 0f;

        if (segmentSeconds > 0f && segmentElapsed >= segmentSeconds)
        {
            return Status.Success;
        }

        return Status.Running;
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
            // 목적지가 남아 있으면 **아직 가는 중이다**(구간 끝). 여기서 멈추면 구간마다 끊겨
            // 4초마다 서다 걷다를 반복한다. 경로와 이동 상태를 그대로 두고 넘긴다.
            //
            // ⚠ 대가: 밤 전환·드래그로 이 브랜치가 중단돼도 Agent가 계속 걷는다. 그 브랜치들이
            //   이동 소유권을 직접 가져가는 설계라(§8.2 드래그는 NavMeshAgent를 끈다) 지금은 문제가
            //   없지만, 소유권을 안 가져가는 브랜치를 추가하면 여기서 정지를 지시해야 한다.
            bool journeyDone = HasDestination == null || !HasDestination.Value;

            if (journeyDone)
            {
                agent.StopMoving();
                agent.SetMoving(false);
            }

            // 여정이 이어지면 다음 진입에서 **여정 시계만** 잇는다. 구간 시계는 언제나 새로 시작한다.
            continueJourney = !journeyDone;
        }

        agent = null;
    }
}
