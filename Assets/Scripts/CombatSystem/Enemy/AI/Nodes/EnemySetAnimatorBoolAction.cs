using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// AnimatorController의 Bool 파라미터를 설정한다. 지속 상태 모션(돌진 스프린트 / 가드 자세)용.
//
// **왜 Trigger가 아닌가.** Unity의 Animator Trigger는 전이가 소비하지 않으면 켜진 채로 남는다.
// 그래서 "시작 트리거 / 해제 트리거" 쌍으로 지속 상태를 다루면 두 가지가 깨진다.
//  · 해제 트리거를 상태 밖에서 쏘면 장전된 채 남아 다음 진입을 즉시 취소한다
//  · 짧은 주기로 반복 갱신되는 패턴(P2는 P2_Duration마다 재발동)에서는 매 사이클
//    자세가 풀렸다 다시 올라가는 트위치가 생긴다
// Bool은 멱등이라 기본 진군 브랜치가 매 사이클 false로 덮어써도 무해하다.
//
// **원복 책임은 기본 진군 브랜치가 진다** — `EnemySetSpeedFactorAction`의 배수 1 복귀와 같은 구조다
// (`BossDesign.md` P1 절). 패턴 브랜치는 `Duration = 0`으로 켜두기만 하고, 패턴이 걸리지 않는
// 사이클에 기본 진군이 끈다. 그래서 `EnemyAccelerateAction`이 상한 초과로 실패해 브랜치가
// 중간에 끊겨도 다음 사이클에 자동 복구된다. **그래프에 기본 진군 브랜치가 없으면 모션이 고착된다.**
//
// `Duration > 0`이면 그 시간 뒤 원복한다(중단되어도 OnEnd가 지나간다).
//
// **원복 대상은 "진입 시점에 읽어둔 값"이 아니라 `Value`의 반대다.** 다른 노드(`EnemySetSpeedFactorAction`
// 등)의 규약과 의도적으로 다르다 — 진입 시점 값을 저장하면 이 노드는 영구히 고착된다.
//
// Unity.Behavior는 노드 종료를 지연 처리한다(`BehaviorGraphModule.m_NodesToEnd`). 패턴이 연속으로
// 재발동하면 새 사이클의 `OnStart`가 이전 사이클의 `OnEnd`보다 먼저 실행될 수 있고, 그러면
// `OnStart`가 자기가 켜둔 true를 "진입 시점 값"으로 저장한다. 뒤늦게 도착한 `OnEnd`는 true로
// 원복하고, 이후 모든 사이클이 같은 일을 반복해 플래그가 영구히 서 있게 된다. 실제로 P2 방어
// 태세의 가드가 이렇게 고착됐다.
//
// 포즈 플래그는 소유자가 하나뿐이라 중첩 스코프가 없다 — 진입 시점 값을 볼 이유가 없고,
// `!Value`로 원복하면 호출 순서와 무관하게 멱등하다.
//
// 1회성 모션(준비 동작 / 봉인 / 소환)은 여전히 Trigger가 맞다. `EnemyPlayAnimationAction`을 쓴다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Set Animator Bool",
    description: "AnimatorController의 Bool 파라미터를 설정한다. Duration이 0보다 크면 그 시간 뒤 원복한다.",
    story: "[Agent] sets animator bool [Parameter] to [Value] for [Duration] seconds",
    category: "Action/Enemy",
    id: "7e26b40fd1c94d3fa85017b6c9e2385d")]
public partial class EnemySetAnimatorBoolAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    // AnimatorController의 Bool 파라미터 이름. Boss_Alien_01 기준 `IsCharging` / `IsGuarding`.
    [SerializeReference] public BlackboardVariable<string> Parameter;

    [SerializeReference] public BlackboardVariable<bool> Value;

    // 유지 시간(초). 0 이하면 즉시 성공하고 원복하지 않는다 — 패턴 브랜치의 기본 사용법이다.
    [SerializeReference] public BlackboardVariable<float> Duration;

    private EnemyAgent agent;
    private string parameterName;
    private bool appliedValue;
    private bool shouldRestore;
    private float elapsed;

    // 파라미터 부재 경고를 노드 인스턴스당 1회만 남기기 위한 래치.
    // 기본 진군 브랜치에 놓이면 매 사이클 도는 노드라 래치가 없으면 콘솔이 잠긴다.
    private bool warnedMissingParameter;

    protected override Status OnStart()
    {
        agent = Agent?.Value;
        shouldRestore = false;

        if (agent == null)
        {
            LogFailure("Enemy Set Animator Bool: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        parameterName = Parameter != null ? Parameter.Value : null;
        bool value = Value != null && Value.Value;

        appliedValue = value;

        // 실패가 아니라 성공으로 흘린다. 이 노드가 기본 진군 브랜치에 놓이므로 실패를 반환하면
        // 패턴 Selector에 성공하는 브랜치가 하나도 없어져 트리 전체가 매 틱 실패한다 —
        // 모션이 안 나오는 것보다 나쁜 결과다. 대신 원인을 1회 남긴다.
        if (!agent.TrySetAnimatorBool(parameterName, value))
        {
            if (!warnedMissingParameter)
            {
                warnedMissingParameter = true;

                Debug.LogWarning($"[{agent.name}] AnimatorController에 Bool 파라미터 " +
                    $"'{parameterName}'가 없어 지속 상태 모션이 재생되지 않습니다. " +
                    "이름 오타이거나 상체 레이어가 있는 컨트롤러가 아닐 수 있습니다.", agent);
            }

            return Status.Success;
        }

        float duration = Duration != null ? Duration.Value : 0f;

        if (duration <= 0f)
        {
            return Status.Success;
        }

        shouldRestore = true;
        elapsed = 0f;

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null)
        {
            return Status.Failure;
        }

        elapsed += Time.deltaTime;

        float duration = Duration != null ? Duration.Value : 0f;

        return elapsed >= duration ? Status.Success : Status.Running;
    }

    // 정상 종료와 상위 컴포지트에 의한 중단 모두 이 경로를 지난다.
    // 원복은 `!appliedValue`다 — 진입 시점 값이 아니다(클래스 주석의 「원복 대상」 참조).
    protected override void OnEnd()
    {
        if (shouldRestore && agent != null)
        {
            agent.TrySetAnimatorBool(parameterName, !appliedValue);
        }

        shouldRestore = false;
        agent = null;
    }
}
