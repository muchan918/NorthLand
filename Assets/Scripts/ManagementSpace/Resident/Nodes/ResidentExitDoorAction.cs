using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 문에서 나와 대열을 벗어난다(#276, R9 등장 · §3.2 퇴장 유예).
//
// ⚠ Selector의 자식이고 **밤 귀가 바로 아래**다. 등장 중이 아니면 Failure로 흘린다.
//
// ── 왜 유예가 필요한가 ─────────────────────────────────────────
//
// 유예가 없으면 문 앞에서 곧바로 BT가 평가된다. 그러면 건물 안에 낀 채로 대화 상대를 찾거나
// 벤치를 향해 벽을 뚫으려 든다(§3.2). 문의 **+Z로 D유닛 직진**하는 동안은 그 평가가 아예 없다.
//
// ── 유예가 끝날 때 목적지를 이미 들고 나간다 ────────────────────
//
// **이게 없으면 문 앞에서 2~5초 서 있는다.** HasDestination이 false인 채 합류하면 뽑기 노드가
// R1 유휴를 걸기 때문이다 — 같은 문에서 나온 주민끼리 붙어 서 있는, 대화가 터지기 가장 좋은 창이 된다.
// 여기서 웨이포인트를 미리 뽑아 두면 곧장 걸어 나가고, 웨이포인트가 여럿이라 서로 다른 방향으로 갈린다.
// 유휴는 첫 목적지에 도착한 뒤에 도는 것이 맞다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Resident Exit Door",
    description: "문의 +Z로 직진해 대열을 벗어난 뒤, 웨이포인트 목적지를 들고 BT에 합류한다. 등장 중이 아니면 Failure.",
    story: "[Agent] walks out of the door",
    category: "Action/Resident",
    id: "d58bae3f63f9433c83af0f24ea417799")]
public partial class ResidentExitDoorAction : Action
{
    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    // 뽑기 노드와 같은 변수를 쓴다 — 여기서 채워 두면 그 노드가 건드리지 않고 통과시킨다.
    [SerializeReference] public BlackboardVariable<Vector3> Destination;

    [SerializeReference] public BlackboardVariable<bool> HasDestination;

    // 문 앞에서 직진할 거리(§3.2의 D). 건물 콜라이더 크기에 종속된다.
    [SerializeReference] public BlackboardVariable<float> ExitDistance;

    // 직진 상한(초). 문 앞이 막혀 있어도 여기서 풀어 준다 — 안 그러면 그 주민은 영영 합류하지 못한다.
    [SerializeReference] public BlackboardVariable<float> MaxSeconds;

    private ResidentAgent agent;
    private Resident self;
    private float elapsed;

    protected override Status OnStart()
    {
        agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Resident Exit Door: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        self = agent.Resident;

        // 등장 중이 아니면 해당 없음. 매 주기 지나가는 정상 경로이므로 로그를 남기지 않는다.
        if (self == null || !self.IsEmerging)
        {
            return Failed();
        }

        Vector3 origin = self.EmergeOrigin;
        float distance = ExitDistance != null ? ExitDistance.Value : 0f;

        // 나온 자리에서 전방으로 D유닛. NavMesh 밖이면 나온 자리 자체를 쓴다 — 도달하지 못할 지점을 잡으면
        // 상한까지 그 자리에 서 있게 된다.
        Vector3 target = origin + self.EmergeForward * distance;

        agent.ResumeMovement();

        if (!agent.TrySetDestination(target))
        {
            agent.TrySetDestination(origin);
        }

        agent.SetMoving(true);
        elapsed = 0f;

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null || self == null)
        {
            return Status.Failure;
        }

        elapsed += Time.deltaTime;

        float maxSeconds = MaxSeconds != null ? MaxSeconds.Value : 0f;
        bool timedOut = maxSeconds > 0f && elapsed >= maxSeconds;

        if (!agent.HasArrived && !timedOut)
        {
            return Status.Running;
        }

        HandOffToStroll();

        return Status.Success;
    }

    protected override void OnEnd()
    {
        // 중단으로 끝났을 수도 있다(밤 전환). 등장 상태를 여기서 반드시 푼다 —
        // 남겨 두면 그 주민은 영영 IsBusy로 남아 대화 상대가 되지 못한다.
        if (self != null)
        {
            self.EndEmerge();
        }

        agent = null;
        self = null;
    }

    // 목적지를 하나 들고 산책 브랜치로 넘긴다. 실패하면 그냥 넘긴다 —
    // 그때는 뽑기 노드가 평소처럼 유휴 후 목적지를 정한다(웨이포인트가 없는 씬 등).
    private void HandOffToStroll()
    {
        if (Destination == null || HasDestination == null)
        {
            return;
        }

        if (!ResidentWaypointRegistry.TryGetRandomWaypoint(out ResidentWaypoint waypoint) ||
            !waypoint.TryGetRandomPoint(out Vector3 point))
        {
            return;
        }

        Destination.Value = point;
        HasDestination.Value = true;
    }

    private Status Failed()
    {
        agent = null;
        self = null;

        return Status.Failure;
    }
}
