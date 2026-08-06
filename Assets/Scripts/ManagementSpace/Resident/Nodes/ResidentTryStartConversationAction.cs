using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 근처 주민에게 말을 걸어 본다(#276, §7.1 대화 세션 성립).
//
// ⚠ Selector의 자식이다. **성립하지 못하면 Failure를 반환해 다음 브랜치(산책)로 흘린다.**
//   보스 규약("조건이 안 맞아 아무것도 안 한 것은 실패가 아니라 성공이다")과 반대로 보이지만, 그 규약은
//   상위가 Sequence일 때의 것이다 — 거기서 Failure는 매 틱 재시도를 뜻하고, 여기서 Failure는
//   "나는 해당 없음, 다음 브랜치로"를 뜻하는 Selector의 정상 신호다.
//
// ── 조우는 대화가 아니다 ────────────────────────────────────────
//
// 조우할 때마다 대화가 열리면 20~30명 밀도에서 마을 전체가 상시 대화 중이 되어 산책·춤·유휴가 화면에서
// 사라진다(§7.1). 그래서 확률로 거르고, **실패도 기억해** 짧은 쿨다운을 건다 — 없으면 나란히 걷는 두 명이
// 구간마다 판정을 반복해 결국 붙는다.
//
// 판정은 이동 노드가 구간을 끊고 넘어올 때 한 번 돈다. 별도 타이머 없이 "조우 1회당 대략 1번"이 성립한다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Resident Try Start Conversation",
    description: "근처의 대화 가능한 주민에게 확률로 말을 건다. 성립하면 세션을 만들고 Success, 아니면 Failure.",
    story: "[Agent] tries to start a conversation",
    category: "Action/Resident",
    id: "5bb26a2eb8d74fa6844856d417a17ed4")]
public partial class ResidentTryStartConversationAction : Action
{
    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    // 이 반경 안에서 상대를 찾는다.
    //
    // 넉넉하게 잡아야 한다. 판정이 이동 구간 경계에서만 돌아 **연속이 아니라 표본**이므로,
    // 반경이 좁으면 스쳐 지나가는 두 명이 판정 사이에 지나가 버려 조우가 거의 성립하지 않는다.
    [SerializeReference] public BlackboardVariable<float> Radius;

    // 조우 1회당 대화가 성립할 기본 확률. 여기에 두 주민의 사교성 평균이 곱해진다.
    [SerializeReference] public BlackboardVariable<float> Chance;

    // 확률 판정에 실패한 상대를 다시 후보로 올리기까지의 시간(초).
    [SerializeReference] public BlackboardVariable<float> FailCooldownSeconds;

    // 주고받을 턴 수의 하한·상한. 세션이 이 수를 채우면 해산한다.
    [SerializeReference] public BlackboardVariable<int> MinTurns;

    [SerializeReference] public BlackboardVariable<int> MaxTurns;

    protected override Status OnStart()
    {
        ResidentAgent agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Resident Try Start Conversation: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        Resident self = agent.Resident;

        // Resident가 없으면 이 브랜치는 구조적으로 성립할 수 없다. 매 구간 로그를 남기면 콘솔이 잠기므로
        // 조용히 흘린다 — 부착 누락 경고는 ResidentAgent.Awake가 1회 남긴다.
        if (self == null || self.Conversation != null)
        {
            return Status.Failure;
        }

        float radius = Radius != null ? Radius.Value : 0f;

        if (!ResidentRegistry.TryFindNearestCandidate(self, radius, out Resident other))
        {
            return Status.Failure;
        }

        // **성공이든 실패든 이 상대를 잠시 후보에서 뺀다.** 실패 기록을 남기지 않으면 다음 구간에 같은
        // 상대로 다시 굴려져, "확률로 거른다"가 "몇 번 굴리면 결국 성립한다"로 무너진다(§7.1).
        // 양쪽 표에 다 남긴다 — 한쪽만 기억하면 반대쪽이 곧바로 말을 건다.
        float failCooldown = FailCooldownSeconds != null ? FailCooldownSeconds.Value : 0f;
        self.Encounters.Mark(other, failCooldown);
        other.Encounters.Mark(self, failCooldown);

        if (!Passes(self, other))
        {
            return Status.Failure;
        }

        int minTurns = MinTurns != null ? MinTurns.Value : 1;
        int maxTurns = MaxTurns != null ? MaxTurns.Value : minTurns;
        int turnCount = maxTurns > minTurns ? Random.Range(minTurns, maxTurns + 1) : minTurns;

        if (!ResidentConversation.TryCreate(self, other, turnCount, out ResidentConversation _))
        {
            // 같은 프레임에 상대가 다른 세션에 들어간 경우다. 정상 경로이므로 조용히 넘긴다.
            return Status.Failure;
        }

        // **상대를 그 자리에 세운다.** 상대의 BT는 아직 산책 브랜치에 있고, 대화 브랜치로 넘어오는 것은
        // Priority Abort가 다음 틱에 처리한다. 그 한 틱 사이에 계속 걸어가면 두 명의 거리가 벌어진다.
        //
        // 이것은 동시에 **선점이 동작하지 않을 때의 안전망**이다. 선점이 조용히 죽어도 상대는 제자리에
        // 서 있고, 자기 이동 구간이 끝나면 대화에 합류한다 — 버그가 아니라 최대 몇 초의 지연으로 열화한다.
        // (걷기 애니메이션도 함께 끈다. 정지한 채 Walk를 틀면 제자리에서 행군하는 그림이 된다.)
        ResidentAgent otherAgent = other.Agent;

        if (otherAgent != null)
        {
            otherAgent.PauseMovement();
            otherAgent.SetMoving(false);
        }

        return Status.Success;
    }

    // 사교성은 두 사람의 평균으로 본다(§7.1 개체차).
    //
    // 곱이 아니라 평균인 이유: 곱으로 하면 조용한 주민(0.6) 둘이 만났을 때 0.36배로 급락해 사실상
    // 절대 대화하지 않는 조합이 생긴다. 평균이면 "말 많은 쪽이 조용한 쪽을 끌어낸다"가 되어 한쪽의
    // 성향이 다른 쪽을 지우지 않는다.
    private bool Passes(Resident self, Resident other)
    {
        float baseChance = Chance != null ? Chance.Value : 0f;

        if (baseChance <= 0f)
        {
            return false;
        }

        float sociability = (self.Sociability + other.Sociability) * 0.5f;

        return Random.value < baseChance * sociability;
    }
}
