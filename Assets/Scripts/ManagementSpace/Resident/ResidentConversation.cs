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

    /// 참가자. **배열이 아니라 List다** — 진행 중 합류(§7.1)로 인원이 늘어난다.
    private readonly List<Slot> slots;

    /// 이 거리보다 안쪽에 비참가자가 서 있으면 "무리가 그를 감싼다"고 본다. 원 반지름에서 주민 반경만큼
    /// 뺀 자리 — 참가자 몸통 바로 안쪽부터가 감싸이는 위치다.
    private const float OutsiderInsetFromRing = 0.6f;

    /// 감싸는 것을 피하려고 중심을 옮길 수 있는 최대 거리. 무제한으로 밀면 무리가 벽 쪽으로 날아가
    /// <see cref="SnapOrKeep"/>이 원을 찌그러뜨린다. 몸통 지름만큼이면 감싸는 그림은 풀린다.
    private const float MaxCenterShift = 1.2f;

    /// 수다에 한 번이라도 들어갔는가. 합류로 Approaching에 되돌아왔을 때 턴 수를 초기화하지 않기 위한 구분이다 —
    /// 초기화하면 사람이 합류할 때마다 대화가 처음부터 다시 시작해 끝나지 않는다.
    private bool talkStarted;

    /// 총 몇 턴을 주고받고 끝낼지. **시간이 아니라 턴 수로 정한다** — 시간으로 끊으면 Talking_1(10.27초)이
    /// 뽑힌 마지막 턴이 중간에 잘린다(§7.2). 턴 수로 하면 항상 온전한 턴에서 끝난다.
    private readonly int turnCount;

    private readonly float createdAt;

    private int speakerIndex;
    private int turnsDone;
    private bool standPointsResolved;

    private ResidentConversation(Resident first, Resident second, int turnCount)
    {
        slots = new List<Slot>(3)
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

            for (int i = 0; i < slots.Count; i++)
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

        for (int i = 0; i < slots.Count; i++)
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

        for (int i = 0; i < slots.Count; i++)
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

        for (int i = 0; i < slots.Count; i++)
        {
            // 빠진 참가자는 기다리지 않는다 — 영원히 도착 표시를 못 하므로 전원 대기가 걸린다.
            // 이탈 자체는 HasLostParticipant가 따로 처리한다.
            if (slots[i].Resident != null && !slots[i].Approached)
            {
                return;
            }
        }

        // 자리를 잡았으면 합류 연출은 끝이다 — 이후 시선은 낀 사람이 아니라 화자를 향한다.
        RecentJoiner = null;

        if (!talkStarted)
        {
            talkStarted = true;

            // 첫 화자는 무작위다. 제안자로 고정하면 말을 건 쪽이 항상 먼저 말하게 되어 규칙이 눈에 보인다.
            // 합류로 되돌아온 경우도 여기로 온다(TryJoin이 talkStarted를 내린다) — 턴이 초기화된다.
            speakerIndex = Random.Range(0, slots.Count);
            turnsDone = 0;
        }
        else
        {
            speakerIndex = Mathf.Clamp(speakerIndex, 0, slots.Count - 1);
        }

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

        for (int i = 0; i < slots.Count; i++)
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
    /// **N명 원주 배치다.** 인접한 두 사람의 간격이 <paramref name="distance"/>가 되도록 반지름을 잡는다:
    /// 현이 `2R·sin(π/N)`이므로 `R = distance / (2·sin(π/N))`. N=2면 R = distance/2라
    /// **기존 2인 중점 대칭과 정확히 같은 결과**가 나온다 — 확장이 기존 그림을 바꾸지 않는다.
    ///
    /// 자리는 가까운 것부터 배정한다. 슬롯 순서대로 주면 두 사람이 서로의 자리로 건너가며 교차한다.
    public void ResolveStandPoints(float distance, float snapDistance)
    {
        if (standPointsResolved)
        {
            return;
        }

        standPointsResolved = true;

        // 살아 있는 참가자만 자리를 받는다. 빠진 슬롯을 세면 원이 헐거워진다.
        var live = new List<Slot>(slots.Count);

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Resident != null)
            {
                live.Add(slots[i]);
            }
        }

        if (live.Count < 2)
        {
            KeepCurrentPositions();
            return;
        }

        Vector3 center = Vector3.zero;

        for (int i = 0; i < live.Count; i++)
        {
            center += live[i].Resident.transform.position;
        }

        center /= live.Count;

        float span = Mathf.Max(0f, distance);
        float radius = span / (2f * Mathf.Sin(Mathf.PI / live.Count));

        // 중심이 지나가던 주민 위에 얹히면 셋이 그를 에워싸며 자리를 잡는다 — 합류(TryJoin)로 자리를
        // 다시 잡을 때 실제로 나오는 그림이다. 원래 둘 사이를 통과 중이던 사람이 새 중심 근처에 있기 때문.
        center = PushCenterOffOutsiders(center, radius);

        Vector3 axis = live[0].Resident.transform.position - center;
        axis.y = 0f;

        // 전원이 한 점에 겹쳐 있으면 방향을 뽑을 수 없다. 한쪽의 정면을 축으로 삼는다.
        if (axis.sqrMagnitude < 0.0001f)
        {
            axis = live[0].Resident.transform.forward;
            axis.y = 0f;
        }

        axis = axis.sqrMagnitude < 0.0001f ? Vector3.forward : axis.normalized;

        var spots = new Vector3[live.Count];

        for (int i = 0; i < live.Count; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(360f * i / live.Count, Vector3.up) * axis;
            spots[i] = center + dir * radius;
        }

        var taken = new bool[live.Count];

        for (int i = 0; i < live.Count; i++)
        {
            Vector3 from = live[i].Resident.transform.position;
            int best = -1;
            float bestSqr = float.MaxValue;

            for (int s = 0; s < spots.Length; s++)
            {
                if (taken[s])
                {
                    continue;
                }

                float d = (spots[s] - from).sqrMagnitude;

                if (d < bestSqr)
                {
                    bestSqr = d;
                    best = s;
                }
            }

            taken[best] = true;
            live[i].StandPoint = SnapOrKeep(spots[best], from, snapDistance);
        }
    }

    /// 무리 중심이 **대화에 참여하지 않는 주민을 감싸지 않도록** 옆으로 비킨다.
    ///
    /// 가장 깊이 들어와 있는 한 명만 본다. 여럿이면 비킨 자리에 또 걸릴 수 있지만, 자리 배치는 세션당
    /// 한 번이라 반복 보정으로 늘어질 이유가 없다 — 최악(정중앙에 두고 에워싸기)만 피하면 된다.
    ///
    /// 옮기는 거리에 상한을 둔다(<see cref="MaxCenterShift"/>). 감싸는 그림을 완전히 없애는 것보다
    /// 원이 벽으로 날아가지 않는 쪽이 중요하다 — 회피물이 없어진 뒤로 감싸임은 **교착이 아니라 그림 문제**다.
    private Vector3 PushCenterOffOutsiders(Vector3 center, float radius)
    {
        float inside = radius - OutsiderInsetFromRing;

        if (inside <= 0f)
        {
            return center;
        }

        IReadOnlyList<Resident> all = ResidentRegistry.Residents;
        Resident nearest = null;
        float nearestSqr = inside * inside;

        for (int i = 0; i < all.Count; i++)
        {
            Resident other = all[i];

            // 참가자는 감싸는 대상이 아니다 — 원을 이루는 것이 그들의 자리다.
            if (other == null || !other.isActiveAndEnabled || IndexOf(other) >= 0)
            {
                continue;
            }

            // 높이는 무시한다 — ResidentRegistry의 질의와 같은 이유(계단·언덕).
            Vector3 delta = other.transform.position - center;
            delta.y = 0f;

            float sqr = delta.sqrMagnitude;

            if (sqr >= nearestSqr)
            {
                continue;
            }

            nearestSqr = sqr;
            nearest = other;
        }

        if (nearest == null)
        {
            return center;
        }

        Vector3 away = center - nearest.transform.position;
        away.y = 0f;

        // 정확히 겹쳐 있으면 방향을 못 뽑는다. 그 사람의 정면을 축으로 삼는다 —
        // 걸어가던 방향이므로 그 앞을 비워 주는 셈이 된다.
        if (away.sqrMagnitude < 0.0001f)
        {
            away = nearest.transform.forward;
            away.y = 0f;
        }

        if (away.sqrMagnitude < 0.0001f)
        {
            away = Vector3.forward;
        }

        float shift = Mathf.Min(inside - Mathf.Sqrt(nearestSqr), MaxCenterShift);

        return center + away.normalized * shift;
    }

    /// 진행 중인 대화에 끼어든다(§7.1 진행 중 합류).
    ///
    /// 합류하면 **인사부터 다시 한다.** 단계를 <see cref="ConversationPhase.Greeting"/>으로 되돌리고
    /// 인사·도착 표시를 전부 지우므로 순서가 이렇게 된다:
    ///
    ///   1. 낀 사람이 인사한다
    ///   2. 원래 대화 중이던 사람들이 **낀 사람을 보면서** 인사한다(<see cref="RecentJoiner"/>가 시선을 몰아 준다)
    ///   3. 세 명이 원주로 자리를 다시 잡는다(인원수에 따라 반지름·각도가 달라져 이미 서 있던 사람도 움직인다)
    ///   4. 턴을 초기화하고 처음부터 다시 시작한다
    ///
    /// 4번의 턴 초기화가 안전한 이유: 인원 상한이 3이라 **한 세션에 합류는 최대 1번**이다
    /// (<see cref="CanAccept"/>가 3에서 막는다). 상한을 4 이상으로 올리면 합류마다 턴이 초기화되어
    /// 대화가 늘어지므로, 그때는 이 규칙을 다시 봐야 한다.
    ///
    /// 헤어지는 중이거나 끝난 세션에는 낄 수 없다.
    public bool TryJoin(Resident newcomer, int maxParticipants)
    {
        if (newcomer == null || newcomer.Conversation != null)
        {
            return false;
        }

        if (Phase == ConversationPhase.Farewell || Phase == ConversationPhase.Ended)
        {
            return false;
        }

        if (slots.Count >= Mathf.Max(2, maxParticipants) || IndexOf(newcomer) >= 0)
        {
            return false;
        }

        slots.Add(new Slot
        {
            Resident = newcomer,
            Joined = true,
        });

        newcomer.Conversation = this;

        RecentJoiner = newcomer;
        standPointsResolved = false;

        // 인사부터 다시 한다 — 전원이 낀 사람을 보고 인사한 뒤에 자리를 잡는다.
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].Greeted = false;
            slots[i].Approached = false;
        }

        // 턴을 초기화한다(합류는 세션당 1회뿐이라 늘어지지 않는다 — 위 주석 4번).
        talkStarted = false;

        // Pending이면 아직 전원이 모이지 않았다는 뜻이라 그대로 둔다 — Join이 마저 처리한다.
        if (Phase != ConversationPhase.Pending)
        {
            Phase = ConversationPhase.Greeting;
        }

        return true;
    }

    /// 합류를 시도한 외부인에게 **전 참가자와의** 조우 쿨다운을 건다(§7.1 재진입 방지).
    ///
    /// ⚠ 한 명(화자 등)에게만 걸면 실효가 없다. 후보 판정(`ResidentRegistry.TryFindNearestJoinable`)은
    ///   **구성원마다** `IsReady`를 보고 통과하는 가장 가까운 사람을 고르므로, 표시가 안 된 다른 구성원을
    ///   통해 같은 세션이 다음 틱에 다시 잡힌다 — 확률로 거른다는 규칙이 "몇 번 지나가면 결국 낀다"로
    ///   무너지고, JoinChance 관측이 무의미해진다. 화자는 턴마다 회전해서 우연히 다 걸릴 일도 드물다.
    ///
    /// 양쪽 표에 남긴다 — 신규 대화 경로가 `self.Mark(other)` + `other.Mark(self)`인 것과 같은 규칙이다.
    public void MarkEncounterWithAll(Resident outsider, float seconds)
    {
        if (outsider == null)
        {
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            Resident member = slots[i].Resident;

            if (member == null || member == outsider)
            {
                continue;
            }

            outsider.Encounters.Mark(member, seconds);
            member.Encounters.Mark(outsider, seconds);
        }
    }

    /// 방금 합류한 참가자. 합류 직후의 인사 동안 **전원의 시선을 이 사람에게 몰아 주는** 데 쓴다.
    /// 자리를 잡기 시작하면(<see cref="MarkApproached"/>) 비운다 — 그 뒤로는 화자를 봐야 한다.
    public Resident RecentJoiner { get; private set; }

    /// 지금 이 세션에 더 낄 수 있는가. 레지스트리가 후보를 거를 때 쓴다.
    public bool CanAccept(int maxParticipants) =>
        Phase != ConversationPhase.Farewell
        && Phase != ConversationPhase.Ended
        && slots.Count < Mathf.Max(2, maxParticipants);

    /// 지금 말하고 있는 참가자. 빠졌으면 null이다.
    public Resident CurrentSpeaker =>
        speakerIndex >= 0 && speakerIndex < slots.Count ? slots[speakerIndex].Resident : null;

    /// 이 참가자가 바라볼 지점. **화자를 본다** — 2인에서는 곧 상대이므로 기존 동작과 같고,
    /// 3인 이상에서 "아무나 한 명"을 보는 것보다 대화로 읽힌다.
    /// 자기가 화자면 나머지의 중심을 본다(한 명만 붙잡고 말하는 그림 방지).
    public bool TryGetFocusPoint(Resident self, out Vector3 point)
    {
        point = Vector3.zero;

        // 합류 직후 인사 동안에는 **전원이 낀 사람을 본다**(요청 순서 2번).
        // 낀 사람 자신은 아래로 흘러 나머지의 중심을 본다.
        if (Phase == ConversationPhase.Greeting && RecentJoiner != null && RecentJoiner != self)
        {
            point = RecentJoiner.transform.position;
            return true;
        }

        Resident speaker = CurrentSpeaker;

        if (Phase != ConversationPhase.Greeting && speaker != null && speaker != self)
        {
            point = speaker.transform.position;
            return true;
        }

        Vector3 sum = Vector3.zero;
        int count = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            Resident other = slots[i].Resident;

            if (other == null || other == self)
            {
                continue;
            }

            sum += other.transform.position;
            count++;
        }

        if (count == 0)
        {
            return false;
        }

        point = sum / count;
        return true;
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

        for (int i = 0; i < slots.Count; i++)
        {
            // 빠진 참가자는 기다리지 않는다 — 영원히 인사 표시를 못 하므로 전원 대기가 걸린다.
            if (slots[i].Resident != null && !slots[i].Greeted)
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

        speakerIndex = (speakerIndex + 1) % slots.Count;
        BeginTurn();
    }

    /// 세션을 끝내고 참가자를 풀어 준다. 남아 있는 참가자 전원에게 상대별 쿨다운을 걸어
    /// 해산 직후 같은 상대와 다시 성립하는 것을 막는다(§7.1 재진입).
    ///
    /// 여러 번 불려도 안전하다 — 두 참가자의 노드가 각자 종료를 처리하므로 실제로 두 번 불린다.
    public void Disband(float cooldownSeconds)
    {
        Phase = ConversationPhase.Ended;

        for (int i = 0; i < slots.Count; i++)
        {
            Resident resident = slots[i].Resident;

            if (resident == null)
            {
                continue;
            }

            for (int j = 0; j < slots.Count; j++)
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
        for (int i = 0; i < slots.Count; i++)
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

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Resident == resident)
            {
                return i;
            }
        }

        return -1;
    }
}
