using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 밤이 되면 가장 가까운 문으로 뛰어가 들어간다(#276, R8 귀가 · §3.3).
//
// ⚠ Selector의 **최상위** 자식이다. 밤은 다른 어떤 행위보다 우선한다 — 대화 중이든 춤추는 중이든
//   전부 끊고 집으로 간다. 위에 얹힌 Priority Abort가 그 중단을 한다(ResidentIsNightCondition).
//
// **걷지 않고 뛴다.** 해가 지면 서둘러 들어가는 그림이라, 이동속도 배수와 Run 클립을 함께 올린다.
// 배수는 OnEnd에서 반드시 1로 되돌린다 — 되돌리지 않으면 아침에 나온 주민이 계속 뛴다.
//
// 도착해도 **스스로 사라지지 않는다.** Resident에 표시만 남기고 소멸은 ResidentSpawner가 프레임 끝에
// 처리한다 — BT 노드가 도는 도중에 자기 GameObject를 비활성화하면 그래프가 자기 Update 위에서 꺼진다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Resident Go Home",
    description: "밤이면 가장 가까운 ResidentDoorPoint로 뛰어가 귀가 표시를 남긴다. 낮이면 Failure.",
    story: "[Agent] runs home for the night",
    category: "Action/Resident",
    id: "230f3217e0da40bda46f0d317d313752")]
public partial class ResidentGoHomeAction : Action
{
    // 씬에 문이 하나도 없을 때의 경고를 1회로 제한한다. 주민 30명이 매 밤 경고를 뱉으면 콘솔이 묻힌다.
    private static bool s_warnedNoDoor;

    /// 플레이 세션 시작마다 되돌린다. 도메인 리로드가 꺼져 있으면 **1회차에 소진된 래치가 그대로 남아**,
    /// 이후 세션에서 문을 지워도 경고가 한 줄도 안 나온다 — 증상은 "주민이 안 들어간다"뿐이라
    /// 이 시스템이 §11.7에 모아 둔 「조용히 실패」에 그대로 해당한다.
    /// 레지스트리 3종이 같은 이유로 같은 처리를 한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_warnedNoDoor = false;

    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    [SerializeReference] public BlackboardVariable<string> RunState;

    [SerializeReference] public BlackboardVariable<float> CrossFadeSeconds;

    // 기준 이동속도에 곱할 배수. 2면 두 배로 뛴다.
    [SerializeReference] public BlackboardVariable<float> SpeedFactor;

    // 문에 도착했다고 볼 거리. NavMeshAgent의 stoppingDistance만으로는 회피에 밀려 애매하게 멈추는 경우가 있다.
    [SerializeReference] public BlackboardVariable<float> ArriveDistance;

    // 상한(초). 도달 불가능한 문이 잡혔을 때 주민이 영영 안 들어가는 것을 막는다.
    [SerializeReference] public BlackboardVariable<float> MaxSeconds;

    private ResidentAgent agent;
    private Resident self;
    private ResidentDoorPoint door;
    private float elapsed;

    protected override Status OnStart()
    {
        agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Resident Go Home: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        self = agent.Resident;

        // 낮에는 이 브랜치가 해당 없음이다. 매 주기 지나가는 정상 경로이므로 로그를 남기지 않는다.
        DayNightManager dayNight = DayNightManager.Instance;

        if (self == null || dayNight == null || dayNight.CurrentPhase != DayNightManager.Phase.Night)
        {
            return Failed();
        }

        // 이미 도착 표시가 남아 있다 = 스포너가 아직 거두지 않았을 뿐이다. 다시 뛰게 하지 않는다 —
        // 안 그러면 거둬 가기 전까지 매 틱 목적지·클립·속도를 새로 걸며 헛돈다.
        if (self.HasArrivedHome)
        {
            return Status.Success;
        }

        if (!ResidentDoorPointRegistry.TryGetNearest(agent.Position, out door))
        {
            if (!s_warnedNoDoor)
            {
                s_warnedNoDoor = true;
                Debug.LogWarning("[주민] 씬에 쓸 수 있는 ResidentDoorPoint가 없어 밤에 귀가하지 못합니다. " +
                    "빈 GameObject에 ResidentDoorPoint를 붙여 문 앞에 배치하세요. (이 경고는 1회만 표시됩니다)");
            }

            return Failed();
        }

        if (!agent.TrySetDestination(door.Position))
        {
            return Failed();
        }

        elapsed = 0f;

        // 대화·춤에서 곧바로 끊겨 들어올 수 있다. 그쪽 노드의 OnEnd가 회전·애니메이션을 이미 되돌렸지만,
        // 이동 재개는 여기서 명시한다 — 멈춰 있던 상태로 목적지만 받으면 제자리에 선다.
        agent.ResumeMovement();
        agent.SpeedFactor = SpeedFactor != null ? SpeedFactor.Value : 1f;
        agent.PlayState(RunState != null ? RunState.Value : null, Fade);

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null || self == null)
        {
            return Status.Failure;
        }

        // ⚠ 아침이 오면 스스로 빠진다.
        //
        // 위에 얹힌 관찰자는 **아래 형제만** 끊는다 — 이 브랜치 자신은 아무도 안 끊어 준다.
        // 그래서 밤이 끝났는지를 여기서 매 틱 확인해야 한다. 빠뜨리면 밤에 Running이던 노드가
        // **아침에 재활성화된 주민에게서 그대로 이어져**, 어젯밤 문을 향해 뛰거나 그 자리에서
        // 도착 판정이 서서 나오자마자 다시 사라진다(실측된 증상).
        DayNightManager dayNight = DayNightManager.Instance;

        if (dayNight == null || dayNight.CurrentPhase != DayNightManager.Phase.Night)
        {
            return Status.Failure;
        }

        elapsed += Time.deltaTime;

        float maxSeconds = MaxSeconds != null ? MaxSeconds.Value : 0f;

        if (maxSeconds > 0f && elapsed >= maxSeconds)
        {
            Debug.LogWarning($"[{agent.name}] 귀가가 {maxSeconds}초를 넘겨 그 자리에서 들어갑니다. " +
                "문이 NavMesh로 닿는 위치인지 확인하세요.", agent);

            self.MarkArrivedHome();
            return Status.Success;
        }

        // 거리로 판정한다. 문 앞은 여럿이 몰리는 자리라 회피에 밀려 stoppingDistance 안으로 못 들어가는
        // 경우가 있는데, 그때 HasArrived만 보면 도착이 서지 않는다.
        float arriveDistance = ArriveDistance != null ? ArriveDistance.Value : 0f;
        Vector3 delta = door != null ? door.Position - agent.Position : Vector3.zero;
        delta.y = 0f;

        bool arrived = door == null ||
            delta.sqrMagnitude <= arriveDistance * arriveDistance ||
            agent.HasArrived;

        if (!arrived)
        {
            return Status.Running;
        }

        self.MarkArrivedHome();

        return Status.Success;
    }

    protected override void OnEnd()
    {
        if (agent != null)
        {
            // 되돌리지 않으면 아침에 나온 주민이 계속 뛴다.
            agent.SpeedFactor = 1f;
            agent.StopMoving();
            agent.ReturnToLocomotion(Fade);
        }

        agent = null;
        self = null;
        door = null;
    }

    private float Fade => CrossFadeSeconds != null ? CrossFadeSeconds.Value : 0f;

    private Status Failed()
    {
        agent = null;
        self = null;
        door = null;

        return Status.Failure;
    }
}
