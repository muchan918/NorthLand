using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 혼자 있을 때 낮은 확률로 춤춘다(#276, R5).
//
// ⚠ Selector의 자식이다. **조건이 안 맞으면 Failure를 반환해 다음 브랜치로 흘린다** — Sequence 아래의
//   "조건 불충족은 Success" 규약과 반대다(ResidentTryStartConversationAction과 같은 이유).
//
// ── 공연(§10)의 선행 형태다 ────────────────────────────────────
//
// 춤은 혼자 하는 행위라 참가자를 묶을 세션 객체가 필요 없지만, **다른 행위를 막는 방식은 공연과 같다** —
// 상태를 Resident가 들고(`IsDancing` → `IsBusy`), 그 상태가 대화 성립을 막는다. 공연이 들어오면
// 같은 자리에 세션 참조가 나란히 붙는다.
//
// 다른 점은 발동 조건뿐이다. 공연은 **정해진 spot**에서만 열리지만 춤은 **산책 중 아무 데서나**,
// 대신 주변에 사람이 없을 때만 시작한다.
//
// ── 왜 "혼자일 때"인가 ─────────────────────────────────────────
//
// 남이 보는 앞에서 갑자기 춤추기 시작하면 앰비언트가 아니라 버그처럼 읽힌다. 그리고 시작 조건을
// **완전히 혼자**로 두어야 "춤추는 중에 사람이 들어오면 부끄러워한다"는 후속 연출이 성립한다 —
// 처음부터 옆에 사람이 있었으면 부끄러워할 계기가 없다.
//
// 그 후속은 이 노드를 고치는 것이 아니라 **위에 브랜치를 하나 더 얹어** 만든다. Priority Abort의
// 감시 조건을 "춤추는 중 && 주변에 사람 있음"으로 두면 춤 브랜치가 중단되고 부끄러움 브랜치가 잡는다
// (대화 합류에 쓴 것과 같은 기계장치, §11.3).
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Resident Dance",
    description: "주변에 사람이 없으면 낮은 확률로 춤춘다. 조건이 안 맞으면 Failure.",
    story: "[Agent] may dance alone",
    category: "Action/Resident",
    id: "138ba2c9889443cf913138e9257a17bf")]
public partial class ResidentDanceAction : Action
{
    // 요청한 상태에 이 시간 안에 도착하지 못하면 이름이 컨트롤러에 없다고 본다.
    // ResidentConverseAction과 같은 값·같은 이유다.
    private const float StateArrivalTimeout = 1.5f;

    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    // 한 번 판정할 때 춤출 확률(0~1).
    [SerializeReference] public BlackboardVariable<float> Chance;

    // "혼자"를 판정하는 반경.
    [SerializeReference] public BlackboardVariable<float> SoloRadius;

    // 이 수를 **넘으면** 춤추지 않는다. 0이면 반경 안에 아무도 없어야 한다.
    [SerializeReference] public BlackboardVariable<int> MaxNeighbors;

    // 춤 클립의 상태 이름.
    [SerializeReference] public BlackboardVariable<string> DanceState;

    [SerializeReference] public BlackboardVariable<float> CrossFadeSeconds;

    // 몇 바퀴 돌고 끝낼지. **시간이 아니라 바퀴 수로 센다** — 시간으로 끊으면 동작 중간에 잘려
    // 춤이 뚝 멎는다. 클립이 루프라 normalizedTime이 1을 넘어 계속 증가하므로 그대로 바퀴 수가 된다.
    [SerializeReference] public BlackboardVariable<int> MinLoops;

    [SerializeReference] public BlackboardVariable<int> MaxLoops;

    // 상한(초). 상태 이름이 틀리는 등으로 진행이 멎었을 때 브랜치가 영구 Running이 되는 것을 막는다.
    [SerializeReference] public BlackboardVariable<float> MaxSeconds;

    private ResidentAgent agent;
    private Resident self;

    private string playingState;
    private float elapsed;
    private float targetLoops;

    // 끝까지 췄는가. OnEnd는 정상 종료와 선점 중단 양쪽을 지나가므로, 어느 쪽인지 여기서 구분한다.
    private bool completed;

    protected override Status OnStart()
    {
        agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Resident Dance: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        self = agent.Resident;

        // Resident가 없으면 상태를 들 곳이 없다. 매 주기 로그를 남기면 콘솔이 잠기므로 조용히 흘린다 —
        // 부착 누락 경고는 ResidentAgent.Awake가 1회 남긴다.
        if (self == null || self.IsBusy)
        {
            return Failed();
        }

        if (!ShouldDance())
        {
            return Failed();
        }

        playingState = DanceState != null ? DanceState.Value : null;

        if (string.IsNullOrEmpty(playingState))
        {
            LogFailure("Resident Dance: DanceState가 비어 있어 춤 클립을 재생할 수 없습니다.");
            return Failed();
        }

        int min = MinLoops != null ? MinLoops.Value : 1;
        int max = MaxLoops != null ? MaxLoops.Value : min;
        targetLoops = Mathf.Max(1, max > min ? Random.Range(min, max + 1) : min);

        elapsed = 0f;
        completed = false;

        // 걷던 것을 멈추되 **경로는 남긴다.** 춤이 끝나면 가던 웨이포인트로 이어 간다
        // (R15 휴식·대화와 같은 방식).
        self.BeginDance();
        agent.PauseMovement();
        agent.PlayState(playingState, Fade);

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

        if (maxSeconds > 0f && elapsed >= maxSeconds)
        {
            Debug.LogWarning($"[{agent.name}] 춤이 {maxSeconds}초를 넘겨 강제 종료합니다. " +
                $"Resident.controller에 '{playingState}' 상태가 있는지 확인하세요.", agent);

            return Finished();
        }

        // 상태에 도착하기 전에는 이전 상태의 진행도가 읽힌다 — 그 값으로 바퀴를 세면 시작도 전에 끝난다.
        if (!agent.IsInState(playingState))
        {
            if (elapsed >= StateArrivalTimeout)
            {
                Debug.LogWarning($"[{agent.name}] 애니메이터 상태 '{playingState}'에 도달하지 못했습니다. " +
                    "Resident.controller에 같은 이름의 상태가 있는지 확인하세요.", agent);

                return Finished();
            }

            return Status.Running;
        }

        if (agent.IsAnimatorInTransition)
        {
            return Status.Running;
        }

        return agent.AnimationNormalizedTime >= targetLoops ? Finished() : Status.Running;
    }

    // 스스로 끝냈다고 표시한다. 이 표시가 없으면 OnEnd가 "선점당했다"로 읽는다.
    private Status Finished()
    {
        completed = true;

        return Status.Success;
    }

    // 정상 종료와 중단 모두 이 경로를 지난다.
    protected override void OnEnd()
    {
        if (self != null)
        {
            // 빠뜨리면 그 주민은 이후 영원히 대화 상대가 되지 못한다.
            //
            // completed가 거짓이면 **선점으로 끊긴 것**이다 — 누군가 다가와 감시 조건이 성립했다는 뜻이라,
            // 다음 틱에 도는 반응 브랜치가 그 사실을 읽을 수 있게 남긴다.
            self.EndDance(!completed);
        }

        if (agent != null)
        {
            // 춤 상태에는 나가는 전이가 없다. 여기서 돌려놓지 않으면 그 포즈로 굳는다(§11.4).
            agent.ReturnToLocomotion(Fade);
        }

        agent = null;
        self = null;
        playingState = null;
    }

    private float Fade => CrossFadeSeconds != null ? CrossFadeSeconds.Value : 0f;

    private bool ShouldDance()
    {
        float chance = Chance != null ? Chance.Value : 0f;

        // 확률을 먼저 굴린다. 반경 질의가 주민 수만큼 도는 선형 탐색이라, 대부분의 주기에서 아예 돌지 않게 한다.
        if (chance <= 0f || Random.value >= chance)
        {
            return false;
        }

        // 좁은 통로에서는 춤도 길을 막는다(#332). 대화와 같은 존을 본다 — 막고 싶은 것은 행위가 아니라
        // **좁은 곳에서 멈춰 서는 것**이라서다.
        //
        // 확률 뒤·반경 질의 앞에 둔다. 존 목록은 주민 목록보다 훨씬 짧고 하나도 없으면 즉시 빠진다.
        if (ResidentNoStopZoneRegistry.Contains(self.transform.position))
        {
            return false;
        }

        float radius = SoloRadius != null ? SoloRadius.Value : 0f;
        int maxNeighbors = MaxNeighbors != null ? MaxNeighbors.Value : 0;

        return ResidentRegistry.CountNearby(self, radius) <= maxNeighbors;
    }

    // 참조를 놓고 실패를 반환한다. 이 경로에서는 BeginDance 전이라 되돌릴 상태가 없다.
    private Status Failed()
    {
        agent = null;
        self = null;

        return Status.Failure;
    }
}
