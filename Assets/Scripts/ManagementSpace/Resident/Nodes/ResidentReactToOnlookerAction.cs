using System.Collections.Generic;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 춤이 끊긴 직후의 반응(#276, R5 후속 「부끄러움」).
//
// ── 지금은 비어 있다 ────────────────────────────────────────────
//
// `ReactionStates`가 비어 있으면 즉시 Failure를 반환한다. **춤 중단은 이 노드 없이도 성립한다** —
// 중단은 Priority Abort가 하고, 이 노드는 그 뒤에 무엇을 보여줄지를 맡는다.
//
// **클립을 authoring하면 그대로 동작한다.** 빌더에서 목록을 `["Surprised", "Embarrassed"]`로 채우면
// 놀람 → 부끄러움이 순서대로 재생된다. 노드를 고칠 필요가 없다.
// (`Surprised`는 이미 라이브러리에 있고, 부끄러움 클립만 Mixamo에서 받으면 된다 — §6 수급 목록.)
//
// ── 왜 플래그를 읽는가 ──────────────────────────────────────────
//
// 감시 조건(ResidentDanceSeenCondition)은 **이 노드가 도는 시점에 이미 거짓이다.** 선점이 춤 브랜치를
// 끝내면서 IsDancing이 꺼졌기 때문이다. 그래서 "방금 춤이 끊겼다"를 Resident.DanceInterrupted가
// 대신 전달한다.
//
// 소비는 **읽는 쪽**이 한다. 반응하지 않기로 한 경우(목록이 비었을 때)에도 지운다 —
// 남겨 두면 나중에 클립을 채운 순간 오래된 기록에 반응이 터진다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Resident React To Onlooker",
    description: "춤이 끊긴 직후 반응 클립을 순서대로 재생한다. 목록이 비었거나 끊긴 적이 없으면 Failure.",
    story: "[Agent] reacts to being seen",
    category: "Action/Resident",
    id: "77a6893e8b714b86bf717e03fcda6afd")]
public partial class ResidentReactToOnlookerAction : Action
{
    // 요청한 상태에 이 시간 안에 도착하지 못하면 이름이 컨트롤러에 없다고 본다.
    // ResidentConverseAction·ResidentDanceAction과 같은 값·같은 이유다.
    private const float StateArrivalTimeout = 1.5f;

    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    // 순서대로 1회씩 재생할 상태 이름. **비어 있으면 이 브랜치는 없는 것과 같다.**
    [SerializeReference] public BlackboardVariable<List<string>> ReactionStates;

    [SerializeReference] public BlackboardVariable<float> CrossFadeSeconds;

    // 상한(초). 상태 이름이 틀리는 등으로 진행이 멎었을 때 브랜치가 영구 Running이 되는 것을 막는다.
    [SerializeReference] public BlackboardVariable<float> MaxSeconds;

    private ResidentAgent agent;
    private Resident self;

    private int index;
    private string playingState;
    private float playingElapsed;
    private float elapsed;

    protected override Status OnStart()
    {
        agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Resident React To Onlooker: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        self = agent.Resident;

        if (self == null)
        {
            return Failed();
        }

        // 반응할 일이 있었는지 먼저 읽고 지운다 — 목록이 비어 있어도 지운다.
        bool interrupted = self.ConsumeDanceInterrupted();

        List<string> states = ReactionStates?.Value;

        if (!interrupted || states == null || states.Count == 0)
        {
            return Failed();
        }

        index = 0;
        elapsed = 0f;

        // 춤추다 멈춘 자리에서 반응한다. 목적지는 그대로라 반응이 끝나면 가던 길을 이어 간다.
        agent.PauseMovement();
        BeginPlay(states[index]);

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null || self == null)
        {
            return Status.Failure;
        }

        elapsed += Time.deltaTime;
        playingElapsed += Time.deltaTime;

        float maxSeconds = MaxSeconds != null ? MaxSeconds.Value : 0f;

        if (maxSeconds > 0f && elapsed >= maxSeconds)
        {
            Debug.LogWarning($"[{agent.name}] 반응이 {maxSeconds}초를 넘겨 강제 종료합니다. " +
                "Resident.controller에 반응 상태 이름이 있는지 확인하세요.", agent);

            return Status.Success;
        }

        if (!IsPlayDone())
        {
            return Status.Running;
        }

        List<string> states = ReactionStates?.Value;
        index++;

        if (states == null || index >= states.Count)
        {
            return Status.Success;
        }

        BeginPlay(states[index]);

        return Status.Running;
    }

    protected override void OnEnd()
    {
        if (agent != null)
        {
            // 반응 상태에는 나가는 전이가 없다. 여기서 돌려놓지 않으면 그 포즈로 굳는다(§11.4).
            agent.ReturnToLocomotion(Fade);
        }

        agent = null;
        self = null;
        playingState = null;
    }

    private float Fade => CrossFadeSeconds != null ? CrossFadeSeconds.Value : 0f;

    private void BeginPlay(string stateName)
    {
        playingState = stateName;
        playingElapsed = 0f;

        if (!string.IsNullOrEmpty(stateName))
        {
            agent.PlayState(stateName, Fade);
        }
    }

    // 물린 상태가 1회 재생을 마쳤는가. ResidentConverseAction과 같은 판정이다 —
    // 이름으로 도착을 확인하므로 **상태 이름이 컨트롤러에 없으면 상한 뒤에 경고를 남기고 넘어간다.**
    private bool IsPlayDone()
    {
        if (string.IsNullOrEmpty(playingState))
        {
            return true;
        }

        if (!agent.IsInState(playingState))
        {
            if (playingElapsed < StateArrivalTimeout)
            {
                return false;
            }

            Debug.LogWarning($"[{agent.name}] 애니메이터 상태 '{playingState}'에 도달하지 못했습니다. " +
                "Resident.controller에 같은 이름의 상태가 있는지 확인하세요.", agent);

            return true;
        }

        if (agent.IsAnimatorInTransition)
        {
            return false;
        }

        return agent.AnimationNormalizedTime >= 1f;
    }

    // 참조를 놓고 실패를 반환한다. 이 경로에서는 아직 아무것도 바꾸지 않았다.
    private Status Failed()
    {
        agent = null;
        self = null;

        return Status.Failure;
    }
}
