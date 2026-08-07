using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// 주민 2명의 대화 세션(#276, R3 인사 · R4 수다 · R12 웃음 · R7 놀람).
///
/// **BT 노드끼리 직접 대화하지 않고 이 객체를 경유한다**(§7.1). 두 주민의 노드가 같은 세션을 읽고
/// 자기 몫만 쓴다. MonoBehaviour가 아니라 plain class다 — 씬에 오브젝트를 만들 이유가 없고,
/// 참가자가 파괴돼도 세션 자체는 남아야 한다.
///
/// ── 왜 티커가 없는가 ────────────────────────────────────────────
///
/// 세션을 매 프레임 굴려 주는 주인이 없다. 진행은 전부 **참가자의 행동이 끝났을 때** 일어난다:
///   <see cref="Join"/> → <see cref="MarkGreeted"/> → <see cref="MarkApproached"/> → <see cref="AdvanceTurn"/> → <see cref="MarkFarewelled"/>
///
/// 한쪽을 티커로 지정하면 그 주민이 배치·드래그로 사라질 때 세션이 멎어, 남은 쪽이 영원히 서 있는다.
/// 진행을 행동에 붙이면 주인이 없어도 굴러가고, 사라진 것은 <see cref="HasLostParticipant"/>로 읽힌다.
///
/// ── 한 번에 한 명만 말한다 ──────────────────────────────────────
///
/// 둘이 동시에 Talking을 재생하면 대화가 아니라 서로 말을 끊고 떠드는 그림이 된다(§7.2).
/// 화자는 <see cref="SpeakerIndex"/> 하나로 표현되고, 청자는 그 값을 읽어 자기가 아니면 듣는 쪽 연기를 한다.
public sealed class ResidentConversation
{
    /// 단계 순서가 곧 연출 순서다 — **멀리서 알아보고 손을 흔든 뒤 다가와 이야기하고, 헤어질 때 또 흔든다.**
    ///
    /// 인사를 다가오기 **앞에** 두는 것이 요점이다. 뒤에 두면 두 사람이 말없이 서로에게 걸어와 코앞에 선
    /// 다음에야 손을 흔들어, 인사가 "만나서 반갑다"가 아니라 "이제 대화를 시작한다"는 신호로 읽힌다.
    public enum ConversationPhase
    {
        /// 참가자가 모이는 중. 먼저 온 쪽이 기다린다.
        Pending,

        /// 멀리서 서로를 보고 손을 흔든다(R3). 양쪽이 다 흔들어야 다음으로 넘어간다.
        Greeting,

        /// 이야기할 거리까지 다가간다. 멀면 좁히고, 이미 붙어 있으면 벌린다.
        Approaching,

        /// 화자 교대로 수다를 떤다(R4 · R12).
        Talking,

        /// 헤어지는 인사. 인사 클립을 그대로 다시 쓴다 — "Bye" 쪽이다.
        Farewell,

        /// 끝났다. 참가자는 이 값을 보고 정리하고 빠진다.
        Ended,
    }

    private sealed class Slot
    {
        public Resident Resident;
        public bool Joined;
        public bool Greeted;
        public bool Approached;
        public bool Farewelled;

        /// 이야기하는 동안 서 있을 자리. Approaching 진입 시 한 번 계산된다.
        public Vector3 StandPoint;
    }

    /// 클립을 직전과 다른 것으로 뽑을 때의 재시도 횟수. 목록에 사실상 한 종류만 있으면 실패하는데,
    /// 그때는 같은 것을 다시 쓰는 것이 맞다 — 무한 루프로 막을 문제가 아니다.
    private const int TalkPickAttempts = 8;

    private readonly Slot[] slots;

    /// 총 몇 턴을 주고받고 끝낼지. **시간이 아니라 턴 수로 정한다** — 시간으로 끊으면 Talking_1(10.27초)이
    /// 뽑힌 마지막 턴이 중간에 잘린다(§7.2). 턴 수로 하면 항상 온전한 턴에서 끝난다.
    private readonly int turnCount;

    private readonly float createdAt;

    private int speakerIndex;
    private int turnsDone;
    private bool standPointsResolved;

    private ResidentConversation(Resident first, Resident second, int turnCount)
    {
        slots = new[]
        {
            new Slot { Resident = first },
            new Slot { Resident = second },
        };

        this.turnCount = Mathf.Max(1, turnCount);
        createdAt = Time.time;
        Phase = ConversationPhase.Pending;
    }

    public ConversationPhase Phase { get; private set; }

    /// 지금 말하고 있는 참가자의 슬롯 번호. <see cref="IsSpeaker"/>로 읽는다.
    public int SpeakerIndex => speakerIndex;

    /// 현재 턴이 시작된 절대 시각. 청자가 웃음 시점(턴의 몇 % 지점)을 계산하는 데 쓴다.
    public float TurnStartedAt { get; private set; }

    /// 현재 턴의 길이(초). **화자가 자기 클립 길이를 재서 넣어 준다** — 청자는 클립이 무엇인지 모르므로
    /// 이 값 없이는 "턴의 30% 지점"을 알 수 없다(§7.2 R12).
    /// 아직 안 들어왔으면 0이고, 그때 청자는 웃지 않는다.
    public float TurnDuration { get; private set; }

    /// 몇 번째 턴인가(0부터). 웃음 쿨다운을 턴 단위로 세는 데 쓴다.
    public int TurnIndex => turnsDone;

    /// 직전에 재생한 수다 클립. 같은 것이 연속되면 반복감이 바로 드러난다(§7.2).
    public string LastTalkState { get; private set; }

    /// 참가자가 **진행 중인 대화에서** 빠졌는가(§7.1 이탈). 파괴 · 비활성 · 세션 참조가 끊긴 것을 모두 잡는다.
    ///
    /// 세 번째 조건이 중요하다 — <see cref="Resident.OnDisable"/>은 세션을 해산시키지 않고 자기 참조만
    /// 놓는다. 세션은 그를 여전히 참가자로 들고 있으므로, 남은 쪽은 "저쪽이 나를 안 보고 있다"로 이탈을 읽는다.
    ///
    /// ⚠ **끝난 세션은 이탈이 아니다.** <see cref="Disband"/>가 양쪽의 참조를 놓기 때문에, 이 조건을 빼면
    ///   정상 종료도 세 번째 조건에 걸린다 — 먼저 정리한 쪽이 세션을 끝내는 순간 **남은 쪽이 R7 놀람을
    ///   재생하며** 대화가 매번 놀람으로 끝난다. 판정을 진행 중인 세션으로 한정하는 것이 유일한 구분점이다.
    public bool HasLostParticipant
    {
        get
        {
            if (Phase == ConversationPhase.Ended)
            {
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                Resident resident = slots[i].Resident;

                if (resident == null || !resident.isActiveAndEnabled || resident.Conversation != this)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// 두 주민을 묶어 세션을 만든다. **성공 시 양쪽의 <see cref="Resident.Conversation"/>을 즉시 채운다** —
    /// 상대의 BT가 아직 산책 중이어도 그 순간부터 "대화 중"이 되어, 다른 주민이 겹쳐 말을 걸 수 없다.
    public static bool TryCreate(Resident proposer, Resident invitee, int turnCount, out ResidentConversation conversation)
    {
        conversation = null;

        if (proposer == null || invitee == null || proposer == invitee)
        {
            return false;
        }

        if (proposer.Conversation != null || invitee.Conversation != null)
        {
            return false;
        }

        conversation = new ResidentConversation(proposer, invitee, turnCount);
        proposer.Conversation = conversation;
        invitee.Conversation = conversation;

        return true;
    }

    public bool IsSpeaker(Resident resident) => IndexOf(resident) == speakerIndex;

    /// 상대 참가자. 2인 세션이므로 하나뿐이고, 빠졌으면 null이다.
    public Resident PartnerOf(Resident resident)
    {
        int self = IndexOf(resident);

        for (int i = 0; i < slots.Length; i++)
        {
            if (i == self)
            {
                continue;
            }

            Resident other = slots[i].Resident;

            if (other != null)
            {
                return other;
            }
        }

        return null;
    }

    /// 이 참가자의 BT가 대화 브랜치에 들어왔음을 알린다. 전원이 합류하면 인사로 넘어간다.
    ///
    /// 멱등하다 — 선점(Priority Abort)으로 브랜치가 재진입하면 다시 불린다.
    public void Join(Resident resident)
    {
        int index = IndexOf(resident);

        if (index < 0 || Phase != ConversationPhase.Pending)
        {
            return;
        }

        slots[index].Joined = true;

        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Joined)
            {
                return;
            }
        }

        Phase = ConversationPhase.Greeting;
    }

    /// 이 참가자가 이야기하는 동안 서 있을 자리. Approaching 동안 여기로 걸어간다.
    public Vector3 StandPointOf(Resident resident)
    {
        int index = IndexOf(resident);

        return index >= 0 ? slots[index].StandPoint : resident != null ? resident.transform.position : Vector3.zero;
    }

    /// 이야기할 자리에 도착했음을 알린다. 전원이 서면 수다로 넘어간다.
    public void MarkApproached(Resident resident)
    {
        int index = IndexOf(resident);

        if (index < 0 || Phase != ConversationPhase.Approaching)
        {
            return;
        }

        slots[index].Approached = true;

        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Approached)
            {
                return;
            }
        }

        // 첫 화자는 무작위다. 제안자로 고정하면 말을 건 쪽이 항상 먼저 말하게 되어 규칙이 눈에 보인다.
        speakerIndex = Random.Range(0, slots.Length);
        turnsDone = 0;
        Phase = ConversationPhase.Talking;
        BeginTurn();
    }

    /// 헤어지는 인사를 마쳤음을 알린다. 전원이 마치면 세션이 끝난다.
    ///
    /// **양쪽을 기다리는 것이 여기서도 필요하다.** 한쪽이 먼저 끝내고 걸어가 버리면 남은 쪽이
    /// 빈 자리에 손을 흔든다.
    public void MarkFarewelled(Resident resident)
    {
        int index = IndexOf(resident);

        if (index < 0 || Phase != ConversationPhase.Farewell)
        {
            return;
        }

        slots[index].Farewelled = true;

        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Farewelled)
            {
                return;
            }
        }

        Phase = ConversationPhase.Ended;
    }

    /// 두 사람이 <paramref name="distance"/>만큼 떨어져 마주 설 자리를 계산한다.
    ///
    /// **왜 필요한가**: 조우 반경 안 어디서든 대화가 성립하므로 거리가 제각각이다. 그대로 두면 **거리가
    /// 0.6까지 붙은 채 이야기하는 쌍이 생긴다** — 치비 체형(키 1.63)에서 그 거리는 머리가 부딪히는
    /// 거리다. `NavMeshAgent`는 둘 다 정지하면 서로를 밀어내지 않으므로 회피에 기대서도 풀리지 않는다.
    /// 반대로 멀리서 인사한 쌍은 좁혀야 한다 — 한 계산이 양쪽을 다 처리한다.
    ///
    /// 중점을 기준으로 **대칭인 두 점**을 잡는다. 각자 자기 위치에서 계산하게 하면 걷는 동안 기준이 흔들려
    /// 두 사람의 목표가 어긋나므로, 인사가 끝난 시점에 세션이 한 번 계산해 고정한다.
    ///
    /// **첫 호출만 유효하다.** 두 참가자가 각자 부르는데, 먼저 부른 쪽이 이미 걷기 시작한 뒤에 다시 계산하면
    /// 중점이 이동해 두 사람의 목표가 어긋난다. 보통 두 호출이 같은 프레임에 오지만(그때는 위치가 같아
    /// 결과도 같다), BT 틱을 개체마다 분산시키면(§9 성능 상한) 프레임이 갈린다 — 그때 조용히 깨진다.
    ///
    /// 2인 전용이다. 3인 이상으로 확장하면 중점 대칭이 아니라 원주 배치가 필요하다(§7.1 TBD).
    public void ResolveStandPoints(float distance, float snapDistance)
    {
        if (standPointsResolved)
        {
            return;
        }

        standPointsResolved = true;

        if (slots.Length != 2 || slots[0].Resident == null || slots[1].Resident == null)
        {
            KeepCurrentPositions();
            return;
        }

        Vector3 a = slots[0].Resident.transform.position;
        Vector3 b = slots[1].Resident.transform.position;
        Vector3 midpoint = (a + b) * 0.5f;

        Vector3 axis = a - b;
        axis.y = 0f;

        // 완전히 겹쳐 있으면 방향을 뽑을 수 없다. 한쪽의 정면을 축으로 삼는다 — 어느 방향이든 대칭이면 된다.
        axis = axis.sqrMagnitude < 0.0001f
            ? slots[0].Resident.transform.forward
            : axis.normalized;

        float half = Mathf.Max(0f, distance) * 0.5f;

        slots[0].StandPoint = SnapOrKeep(midpoint + axis * half, a, snapDistance);
        slots[1].StandPoint = SnapOrKeep(midpoint - axis * half, b, snapDistance);
    }

    /// 합류를 기다리다 지쳤는지 본다. 참이면 세션을 조용히 끝낸다 —
    /// 상대가 끝내 오지 않은 것은 이탈이 아니므로 R7을 띄우지 않는다.
    ///
    /// 이 상한이 없으면, 선점이 어떤 이유로든 동작하지 않을 때 먼저 도착한 쪽이 영원히 서 있는다.
    public bool HasPendingTimedOut(float timeoutSeconds) =>
        Phase == ConversationPhase.Pending && timeoutSeconds > 0f && Time.time - createdAt >= timeoutSeconds;

    /// 이 참가자가 인사(R3)를 마쳤음을 알린다. 전원이 마치면 다가가기로 넘어간다.
    ///
    /// **양쪽을 기다리는 것이 요점이다.** 먼저 온 쪽이 혼자 흔들고 끝내면, 뒤늦게 합류한 쪽이 아무도
    /// 안 보는 데서 손을 흔든다.
    public void MarkGreeted(Resident resident)
    {
        int index = IndexOf(resident);

        if (index < 0 || Phase != ConversationPhase.Greeting)
        {
            return;
        }

        slots[index].Greeted = true;

        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Greeted)
            {
                return;
            }
        }

        Phase = ConversationPhase.Approaching;
    }

    /// 다음에 재생할 수다 클립을 뽑는다(§7.2).
    ///
    /// **가중치는 목록에 여러 번 넣어 표현한다.** 예: [Talking_2, Talking_3, Talking_2, Talking_3, Talking_1]이면
    /// Talking_1이 1/5로 나온다. 가중치 전용 파라미터를 두면 목록과 길이를 맞춰야 하는 축이 하나 늘고,
    /// 어긋났을 때 조용히 틀린다. 웨이포인트에서 "인기를 올리려면 같은 자리에 하나 더 놓는다"고 정한 것과 같은 방식이다.
    ///
    /// Talking_1은 10.27초로 나머지의 2.6배라, 같은 비율로 넣으면 한 사람이 10초를 독점하는 턴이 자주 나온다.
    public string PickTalkState(IList<string> states)
    {
        if (states == null || states.Count == 0)
        {
            return null;
        }

        string picked = states[Random.Range(0, states.Count)];

        // 직전과 같은 것이 나오면 다시 뽑는다. 목록에 한 종류만 있으면 재시도가 소진되고 같은 것이 나오는데,
        // 그때는 그것이 유일한 선택이므로 맞다.
        for (int i = 0; i < TalkPickAttempts && picked == LastTalkState; i++)
        {
            picked = states[Random.Range(0, states.Count)];
        }

        LastTalkState = picked;
        return picked;
    }

    /// 화자가 자기 클립 길이를 등록한다. 청자의 웃음 시점 계산에 쓰인다.
    public void SetTurnDuration(float seconds)
    {
        if (seconds > 0f)
        {
            TurnDuration = seconds;
        }
    }

    /// 화자가 자기 턴을 마쳤음을 알린다. 화자·청자를 교대하고, 정해진 턴 수를 채웠으면 끝낸다.
    ///
    /// **턴이 끝나는 시점에만 종료를 판정한다**(§7.2) — 중간에 끊으면 말하다 마는 그림이 된다.
    public void AdvanceTurn(Resident resident)
    {
        if (Phase != ConversationPhase.Talking || !IsSpeaker(resident))
        {
            return;
        }

        turnsDone++;

        if (turnsDone >= turnCount)
        {
            // 곧바로 끝내지 않는다 — 헤어지는 인사를 한 박자 둔다. 없으면 말이 끝나자마자 둘이 등을 돌려
            // 각자 갈 길을 가서, 대화가 끊긴 것처럼 보인다.
            Phase = ConversationPhase.Farewell;
            return;
        }

        speakerIndex = (speakerIndex + 1) % slots.Length;
        BeginTurn();
    }

    /// 세션을 끝내고 참가자를 풀어 준다. 남아 있는 참가자 전원에게 상대별 쿨다운을 걸어
    /// 해산 직후 같은 상대와 다시 성립하는 것을 막는다(§7.1 재진입).
    ///
    /// 여러 번 불려도 안전하다 — 두 참가자의 노드가 각자 종료를 처리하므로 실제로 두 번 불린다.
    public void Disband(float cooldownSeconds)
    {
        Phase = ConversationPhase.Ended;

        for (int i = 0; i < slots.Length; i++)
        {
            Resident resident = slots[i].Resident;

            if (resident == null)
            {
                continue;
            }

            for (int j = 0; j < slots.Length; j++)
            {
                if (j != i)
                {
                    // 양쪽 표에 다 남긴다. 한쪽만 기억하면 반대쪽이 곧바로 다시 말을 건다.
                    resident.Encounters.Mark(slots[j].Resident, cooldownSeconds);
                }
            }

            if (resident.Conversation == this)
            {
                resident.Conversation = null;
            }
        }
    }

    private void KeepCurrentPositions()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Resident != null)
            {
                slots[i].StandPoint = slots[i].Resident.transform.position;
            }
        }
    }

    /// 계산한 자리를 NavMesh 위로 끌어온다. 실패하면 **원래 자리를 그대로 쓴다** —
    /// 벽 안이나 절벽 밖으로 목적지를 잡으면 도달하지 못해 자리잡기가 상한까지 늘어진다.
    private static Vector3 SnapOrKeep(Vector3 candidate, Vector3 fallback, float snapDistance)
    {
        return NavMesh.SamplePosition(candidate, out NavMeshHit hit, Mathf.Max(0.01f, snapDistance), NavMesh.AllAreas)
            ? hit.position
            : fallback;
    }

    private void BeginTurn()
    {
        TurnStartedAt = Time.time;

        // 길이는 화자가 클립을 물고 나서 넣어 준다. 그전까지는 0이고, 청자는 0을 "아직 모른다"로 읽어 웃지 않는다.
        TurnDuration = 0f;
    }

    private int IndexOf(Resident resident)
    {
        if (resident == null)
        {
            return -1;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Resident == resident)
            {
                return i;
            }
        }

        return -1;
    }
}
