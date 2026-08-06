using System.Collections.Generic;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 대화 세션을 끝까지 연기한다(#276, R3 인사 · R4 수다 · R12 웃음 · R7 놀람).
//
// 세션이 없으면 즉시 Failure — Selector가 다음 브랜치로 흘린다. 이것이 대화 브랜치의 진입 게이트다
// (ResidentHasConversationCondition은 게이트가 아니라 선점용 감시 조건이다).
//
// ── 두 주민이 같은 노드를 각자 돈다 ────────────────────────────
//
// 참가자마다 자기 BT에서 이 노드가 돌고, 공유되는 것은 ResidentConversation 하나다. 그래서 이 노드는
// **자기 몫만 연기하고, 진행 상황은 세션에 보고**한다:
//   합류했다 → Join / 인사를 마쳤다 → MarkGreeted / 내 턴이 끝났다 → AdvanceTurn
// 상대가 무엇을 하고 있는지는 세션의 Phase와 SpeakerIndex로만 안다.
//
// ── 한 번에 한 명만 말한다 ──────────────────────────────────────
//
// 세션의 SpeakerIndex가 나를 가리키면 말하고(Talking_n), 아니면 듣는다(Idle + 확률적 웃음).
// 화자는 자기 클립이 한 바퀴 돌면 AdvanceTurn으로 교대를 요청한다 — **턴이 끝나는 시점에만** 판정하므로
// 말하다 마는 그림이 나오지 않는다(§7.2).
//
// ⚠ 목적지를 건드리지 않는다. PauseMovement는 경로를 남기므로(StopMoving은 ResetPath까지 한다),
//   대화가 끝나면 가던 웨이포인트로 그대로 이어 간다. R15 휴식과 같은 이유·같은 방식이다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Resident Converse",
    description: "대화 세션을 인사→수다→해산까지 연기한다. 세션이 없으면 Failure. 상대가 빠지면 놀란 뒤 해산한다.",
    story: "[Agent] converses",
    category: "Action/Resident",
    id: "cd7f3b9a6a2f41319c962191321ccf1d")]
public partial class ResidentConverseAction : Action
{
    // 요청한 상태에 이 시간 안에 도착하지 못하면 이름이 컨트롤러에 없다고 본다.
    // 페이드 시간과 무관하게 넉넉한 값이면 되고, 클립 길이와는 상관없다 — 도착 여부만 보기 때문이다.
    private const float StateArrivalTimeout = 1.5f;

    // 대화 자리를 NavMesh 위로 끌어올 때 허용하는 거리. 두 사람 사이 중점 근방이라 보통 한 번에 붙고,
    // 벽에 붙어 조우한 경우에만 쓰인다. 실패하면 원래 자리를 그대로 쓴다.
    private const float StandSnapDistance = 1.5f;

    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    // 수다 클립의 상태 이름 목록(§7.2). **가중치는 같은 이름을 여러 번 넣어 표현한다** —
    // Talking_1이 10.27초로 나머지의 2.6배라 같은 비율로 두면 한 사람이 10초를 독점하는 턴이 자주 나온다.
    [SerializeReference] public BlackboardVariable<List<string>> TalkStates;

    // 인사(R3) · 웃음(R12) · 놀람(R7)의 상태 이름.
    [SerializeReference] public BlackboardVariable<string> GreetState;

    [SerializeReference] public BlackboardVariable<string> LaughState;

    [SerializeReference] public BlackboardVariable<string> SurprisedState;

    // 상태 전환에 쓰는 크로스페이드 길이(초). Mixamo 대화 클립은 시작·끝이 중립 서 있는 자세라
    // 짧게 이어도 튀지 않는다(§7.2).
    [SerializeReference] public BlackboardVariable<float> CrossFadeSeconds;

    // 마주 봤다고 인정하는 각도. 이 안에 들어오면 인사를 시작한다.
    [SerializeReference] public BlackboardVariable<float> FaceToleranceDegrees;

    // 이야기하는 동안 두 주민 사이의 거리. 인사를 마치고 이 거리까지 다가간다.
    [SerializeReference] public BlackboardVariable<float> StandDistance;

    // 다가가기 상한(초). 목표 지점에 도달하지 못해도 이 시간이 지나면 그 자리에서 이야기를 시작한다.
    [SerializeReference] public BlackboardVariable<float> ApproachTimeoutSeconds;

    // 청자가 한 턴에 웃을 확률. **턴당 1회만 굴린다** — 초당 굴리면 10초 턴에서 10번 굴려져 거의 매번 웃는다.
    [SerializeReference] public BlackboardVariable<float> LaughChance;

    // 턴의 몇 %가 지난 뒤부터 웃어도 되는지(0~1). 턴 시작 직후에 웃으면 아직 아무 말도 안 들었는데 웃는 꼴이다.
    [SerializeReference] public BlackboardVariable<float> LaughAfterTurnFraction;

    // 웃은 뒤 몇 턴을 쉬는지. 1이면 바로 다음 청자 턴에는 웃지 않는다.
    [SerializeReference] public BlackboardVariable<int> LaughTurnCooldown;

    // 상대의 합류를 기다리는 상한(초). 넘기면 조용히 해산한다 — 오지 않은 것은 이탈이 아니므로 놀라지 않는다.
    [SerializeReference] public BlackboardVariable<float> PendingTimeoutSeconds;

    // 해산 후 같은 상대와 다시 성립하지 않는 시간(초). 없으면 두 명이 영원히 인사만 한다.
    [SerializeReference] public BlackboardVariable<float> DisbandCooldownSeconds;

    // 브랜치 전체의 상한(초). 어떤 이유로든 진행이 멎었을 때 주민이 영구히 서 있는 것을 막는다.
    [SerializeReference] public BlackboardVariable<float> MaxSeconds;

    private ResidentAgent agent;
    private Resident self;
    private ResidentConversation session;

    private float elapsed;

    // 지금 물려 있는 일회성 상태와 그 경과 시간. null이면 유휴/걷기 축에 있다는 뜻이다.
    private string playingState;
    private float playingElapsed;

    private bool approachStarted;
    private float approachElapsed;

    private bool greetPlayed;
    private bool greetReported;

    private bool farewellPlayed;
    private bool farewellReported;

    // 이미 준비를 끝낸 턴 번호. 세션의 TurnIndex가 바뀌면 화자·청자 역할이 바뀐 것이므로 다시 준비한다.
    private int handledTurn;
    private bool durationReported;
    private bool laughRolled;
    private bool laughing;

    // 웃음 쿨다운. "한 번도 안 웃었다"를 센티넬 턴 번호로 표현하면 뺄셈이 오버플로하므로 플래그로 나눈다.
    private bool hasLaughed;
    private int lastLaughTurn;

    private bool surprised;

    protected override Status OnStart()
    {
        agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Resident Converse: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        self = agent.Resident;
        session = self != null ? self.Conversation : null;

        // 세션이 없다 = 이 브랜치는 해당 없음. Selector가 다음 브랜치로 흘린다.
        // 매 주기 지나가는 정상 경로이므로 로그를 남기지 않는다.
        if (session == null || session.Phase == ResidentConversation.ConversationPhase.Ended)
        {
            return Status.Failure;
        }

        elapsed = 0f;
        playingState = null;
        playingElapsed = 0f;
        approachStarted = false;
        approachElapsed = 0f;
        greetPlayed = false;
        greetReported = false;
        farewellPlayed = false;
        farewellReported = false;
        handledTurn = -1;
        durationReported = false;
        laughRolled = false;
        laughing = false;
        hasLaughed = false;
        lastLaughTurn = 0;
        surprised = false;

        // 걷던 것을 멈춘다.
        //
        // 산책 목적지를 잃을까 걱정할 필요는 없다 — 목적지는 Blackboard(`Destination`)에 있고 이동 노드가
        // 다음 진입 때 다시 지정한다. 그래서 다가가기(Approaching)에서 NavMeshAgent의 경로를
        // 마음대로 갈아 써도 대화가 끝나면 가던 웨이포인트로 이어 간다.
        agent.PauseMovement();
        agent.ReturnToLocomotion(Fade);

        // 회전 소유권을 NavMeshAgent에 돌려 둔다. 인사·수다 단계에서 FaceTowards가 다시 가져가고,
        // 다가가는 구간에는 진행 방향을 봐야 하므로 Agent가 들고 있어야 한다.
        agent.ReleaseRotation();

        // 멱등하다. 선점으로 이 브랜치가 재진입하면 다시 불린다.
        session.Join(self);

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null || self == null || session == null)
        {
            return Status.Failure;
        }

        elapsed += Time.deltaTime;

        if (playingState != null)
        {
            playingElapsed += Time.deltaTime;
        }

        float maxSeconds = MaxSeconds != null ? MaxSeconds.Value : 0f;

        if (maxSeconds > 0f && elapsed >= maxSeconds)
        {
            Debug.LogWarning($"[{agent.name}] 대화가 {maxSeconds}초를 넘겨 강제 해산합니다. " +
                "수다 클립 상태 이름이 AnimatorController에 있는지 확인하세요.", agent);

            return Finish();
        }

        // ── 이탈 감지가 최우선이다(§7.1) ──
        // 상대가 사라졌으면 남은 쪽이 놀란다. 다른 무엇보다 먼저 봐야 한다 — 아래 단계들은 상대가
        // 살아 있다고 가정하고 짜여 있다.
        //
        // 정상 종료와 구분되는 것은 세션이 보장한다(HasLostParticipant는 Ended에서 거짓이다).
        // 그 구분이 없으면 대화가 매번 놀람으로 끝난다 — 먼저 정리한 쪽이 양쪽 참조를 놓기 때문이다.
        if (!surprised && session.HasLostParticipant)
        {
            surprised = true;
            BeginPlay(SurprisedState != null ? SurprisedState.Value : null);
        }

        if (surprised)
        {
            return IsPlayDone() ? Finish() : Status.Running;
        }

        Resident partner = session.PartnerOf(self);

        // 다가가는 중에는 상대를 보지 않는다 — 걸어가는 방향을 봐야 한다.
        // 그 밖의 단계에서는 계속 상대를 향한다. NavMeshAgent는 정지 중에 회전하지 않으므로 여기서 직접 돌린다.
        if (partner != null && session.Phase != ResidentConversation.ConversationPhase.Approaching)
        {
            agent.FaceTowards(partner.transform.position);
        }

        switch (session.Phase)
        {
            case ResidentConversation.ConversationPhase.Pending:
                // 먼저 도착한 쪽이 기다린다. 상대의 BT는 Priority Abort가 다음 틱에 이 브랜치로 끌어온다.
                return session.HasPendingTimedOut(PendingTimeout) ? Finish() : Status.Running;

            case ResidentConversation.ConversationPhase.Greeting:
                return UpdateGreeting(partner);

            case ResidentConversation.ConversationPhase.Approaching:
                return UpdateApproaching();

            case ResidentConversation.ConversationPhase.Talking:
                return UpdateTalking();

            case ResidentConversation.ConversationPhase.Farewell:
                return UpdateFarewell();

            default:
                return Finish();
        }
    }

    protected override void OnEnd()
    {
        if (agent != null)
        {
            // 회전 소유권을 반납한다. 빠뜨리면 이후로 주민이 옆걸음으로 걷는다.
            agent.ReleaseRotation();

            // 수다·웃음 상태에는 나가는 전이가 없다. 여기서 돌려놓지 않으면 그 포즈로 굳는다.
            // 중단(선점)으로 끝난 경우에도 지나가는 유일한 경로다.
            agent.ReturnToLocomotion(Fade);
        }

        agent = null;
        self = null;
        session = null;
    }

    // ── 단계별 진행 ─────────────────────────────

    // 인사를 마치고 이야기할 거리까지 다가간다.
    //
    // **인사 뒤에 온다는 것이 요점이다.** 조우 반경(넓게 잡는다)에서 알아보고 손을 흔든 다음 걸어와 서므로
    // "멀리서 알아보고 다가온다"가 된다. 순서를 뒤집으면 말없이 코앞까지 걸어와서야 손을 흔들고,
    // 무엇보다 **판정이 구간마다 도는 표본이라 롤이 성공하기 전에 이미 부딪힌다** — 부딪혔다가 물러나서
    // 인사하는 그림이 나온다.
    //
    // 거리를 좁히는 것과 벌리는 것을 같은 계산이 처리한다. 조우 반경 안 어디서든 성립하므로 거리가
    // 제각각이고, 붙어 있던 쌍은 **머리가 부딪히는 0.6까지** 갈 수 있다(NavMeshAgent radius 0.3 × 2).
    // 둘 다 정지한 Agent는 서로를 밀어내지 않으므로 회피에 기대서도 풀리지 않는다.
    private Status UpdateApproaching()
    {
        if (!approachStarted)
        {
            approachStarted = true;
            approachElapsed = 0f;

            float distance = StandDistance != null ? StandDistance.Value : 0f;

            // 두 참가자가 각자 부르지만 세션이 첫 호출만 받는다 — 먼저 부른 쪽이 걷기 시작한 뒤에
            // 다시 계산되면 중점이 이동해 두 목표가 어긋난다. 소유자를 정하지 않아도 결과가 하나다.
            session.ResolveStandPoints(distance, StandSnapDistance);

            Vector3 target = session.StandPointOf(self);

            // 이미 제자리면 목적지 지정이 즉시 도착으로 판정된다 — 따로 분기하지 않는다.
            agent.ResumeMovement();
            agent.TrySetDestination(target);
            agent.SetMoving(true);

            return Status.Running;
        }

        approachElapsed += Time.deltaTime;

        float timeout = ApproachTimeoutSeconds != null ? ApproachTimeoutSeconds.Value : 0f;
        bool timedOut = timeout > 0f && approachElapsed >= timeout;

        if (!agent.HasArrived && !timedOut)
        {
            return Status.Running;
        }

        // 도달했든 상한에 걸렸든 여기서 선다. 상한에 걸린 경우는 조금 어긋난 거리로 이야기하게 되는데,
        // 못 간 자리를 계속 노리며 서 있는 것보다 낫다.
        agent.PauseMovement();
        agent.SetMoving(false);
        session.MarkApproached(self);

        return Status.Running;
    }

    // 헤어지는 인사. **인사 클립을 그대로 다시 쓴다** — "Bye" 쪽이다.
    //
    // 없으면 마지막 말이 끝나는 순간 둘이 등을 돌려 각자 갈 길을 가서, 대화가 마무리된 것이 아니라
    // 끊긴 것처럼 보인다. 클립을 새로 받을 필요가 없는 데 비해 얻는 것이 크다.
    private Status UpdateFarewell()
    {
        if (!farewellPlayed)
        {
            farewellPlayed = true;
            BeginPlay(GreetState != null ? GreetState.Value : null);

            return Status.Running;
        }

        if (!farewellReported)
        {
            if (!IsPlayDone())
            {
                return Status.Running;
            }

            farewellReported = true;
            EndPlay();

            // 양쪽이 다 흔들어야 끝난다. 먼저 끝낸 쪽이 걸어가 버리면 남은 쪽이 빈 자리에 손을 흔든다.
            session.MarkFarewelled(self);
        }

        return Status.Running;
    }

    // 멀리서 알아보고 손을 흔든다(R3). **다가가기보다 먼저 온다** — 순서가 이 연출의 전부다.
    private Status UpdateGreeting(Resident partner)
    {
        if (!greetPlayed)
        {
            // 등을 돌린 채 손을 흔들지 않는다. 다 돌아섰을 때 시작한다(§7.1 「서로 마주 보고 정지」).
            float tolerance = FaceToleranceDegrees != null ? FaceToleranceDegrees.Value : 15f;

            if (partner != null && !agent.IsFacing(partner.transform.position, tolerance))
            {
                return Status.Running;
            }

            greetPlayed = true;
            BeginPlay(GreetState != null ? GreetState.Value : null);

            return Status.Running;
        }

        if (!greetReported)
        {
            if (!IsPlayDone())
            {
                return Status.Running;
            }

            greetReported = true;
            EndPlay();

            // 양쪽이 다 흔들면 세션이 다가가기로 넘어간다. 혼자 끝내고 넘어가면 뒤늦게 합류한 쪽이
            // 아무도 안 보는 데서 손을 흔든다.
            session.MarkGreeted(self);
        }

        return Status.Running;
    }

    private Status UpdateTalking()
    {
        bool speaker = session.IsSpeaker(self);
        int turn = session.TurnIndex;

        // 턴이 바뀌었다 = 역할이 바뀌었다. 화자·청자를 새로 준비한다.
        //
        // 청자가 웃는 도중에 자기 턴이 오면 여기서 웃음이 끊기고 수다로 넘어간다. 그래서 "화자가 바뀐
        // 뒤에도 웃고 있다"(§7.2가 우려한 그림)가 구조적으로 나오지 않는다 — 별도 잔여시간 검사가 필요 없다.
        if (turn != handledTurn)
        {
            handledTurn = turn;
            durationReported = false;
            laughRolled = false;
            laughing = false;

            if (speaker)
            {
                string state = session.PickTalkState(TalkStates?.Value);

                if (string.IsNullOrEmpty(state))
                {
                    LogFailure("Resident Converse: TalkStates가 비어 있어 수다를 재생할 수 없습니다.");
                    Finish();
                    return Status.Failure;
                }

                BeginPlay(state);
            }
            else
            {
                EndPlay();
            }

            return Status.Running;
        }

        return speaker ? UpdateSpeaker() : UpdateListener(turn);
    }

    private Status UpdateSpeaker()
    {
        // 클립에 도착한 뒤에 길이를 읽어 세션에 넣는다. 전이 중에는 **이전 상태의** 클립이 읽힌다.
        // 이 값이 있어야 청자가 "턴의 30% 지점"을 계산할 수 있다(§7.2 R12).
        if (!durationReported && agent.IsInState(playingState) && !agent.IsAnimatorInTransition)
        {
            session.SetTurnDuration(agent.CurrentStateLength);
            durationReported = true;
        }

        if (!IsPlayDone())
        {
            return Status.Running;
        }

        // 한 바퀴 돌았다 → 교대를 요청한다. 정해진 턴 수를 채웠으면 세션이 Ended로 넘어가고,
        // 다음 틱에 위 switch가 해산을 처리한다.
        session.AdvanceTurn(self);

        return Status.Running;
    }

    private Status UpdateListener(int turn)
    {
        if (laughing)
        {
            if (!IsPlayDone())
            {
                return Status.Running;
            }

            laughing = false;
            EndPlay();

            return Status.Running;
        }

        if (!laughRolled && CanLaughNow(turn))
        {
            laughRolled = true;

            float chance = LaughChance != null ? LaughChance.Value : 0f;

            if (Random.value < chance)
            {
                laughing = true;
                hasLaughed = true;
                lastLaughTurn = turn;
                BeginPlay(LaughState != null ? LaughState.Value : null);
            }
        }

        return Status.Running;
    }

    // 웃어도 되는 시점인가(§7.2).
    private bool CanLaughNow(int turn)
    {
        int cooldown = LaughTurnCooldown != null ? LaughTurnCooldown.Value : 0;

        // 연속 턴마다 웃으면 리액션이 아니라 습관이 된다.
        if (hasLaughed && turn - lastLaughTurn <= cooldown)
        {
            return false;
        }

        float duration = session.TurnDuration;

        // 화자가 아직 길이를 넣지 않았다 = 클립이 물리기 전이다. 0을 "짧은 턴"으로 읽으면 시작 즉시 웃는다.
        if (duration <= 0f)
        {
            return false;
        }

        float fraction = LaughAfterTurnFraction != null ? LaughAfterTurnFraction.Value : 0f;

        return Time.time - session.TurnStartedAt >= duration * fraction;
    }

    // ── 헬퍼 ─────────────────────────────

    private float Fade => CrossFadeSeconds != null ? CrossFadeSeconds.Value : 0f;

    private float PendingTimeout => PendingTimeoutSeconds != null ? PendingTimeoutSeconds.Value : 0f;

    private void BeginPlay(string stateName)
    {
        playingState = stateName;
        playingElapsed = 0f;

        if (!string.IsNullOrEmpty(stateName))
        {
            agent.PlayState(stateName, Fade);
        }
    }

    // 일회성 상태를 놓고 유휴로 돌아온다.
    private void EndPlay()
    {
        playingState = null;
        playingElapsed = 0f;
        agent.ReturnToLocomotion(Fade);
    }

    // 물린 상태가 1회 재생을 마쳤는가.
    //
    // 도착 여부를 이름으로 확인하므로, **상태 이름이 컨트롤러에 없으면 영원히 도착하지 못한다.**
    // 그 경우를 상한으로 잡아 경고를 남기고 넘긴다 — 없으면 오타 하나에 주민이 굳은 채 서 있는다.
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

    // 세션을 끝내고 브랜치를 놓는다. 두 참가자가 각자 부르므로 Disband는 멱등하다.
    private Status Finish()
    {
        float cooldown = DisbandCooldownSeconds != null ? DisbandCooldownSeconds.Value : 0f;
        session.Disband(cooldown);

        return Status.Success;
    }
}
