using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 애니메이션 트리거를 발동한다(#234). P1 돌진의 준비 모션.
//
// WaitForEnd면 재생이 끝날 때까지 Running을 유지한다. 종료 판정은 normalizedTime 폴링이다 —
// AnimationEvent 방식은 클립마다 이벤트를 심어야 해서 아직 존재하지 않는 보스
// AnimatorController(#235)의 저작 부담을 노드 쪽으로 떠넘긴다.
//
// 트리거 직후 몇 프레임은 전이 중이라 이전 상태가 읽힌다. 그래서 "전이가 한 번 끝난 뒤"부터
// normalizedTime을 보기 시작한다. 그러지 않으면 이전 상태가 이미 1을 넘겨 있어
// 준비 모션이 시작도 전에 끝난 것으로 오판된다.
//
// MaxWaitSeconds는 노드 대장 표에 없던 안전장치다. 트리거 이름이 컨트롤러에 없으면
// 전이가 일어나지 않아 영구 Running이 되고 P1 시퀀스 전체가 멎는다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Play Animation",
    description: "애니메이션 트리거를 발동한다. WaitForEnd면 재생이 끝날 때까지 대기한다.",
    story: "[Agent] plays animation [Trigger]",
    category: "Action/Enemy",
    id: "ec71295f18e94fedbeef4fdd8532819a")]
public partial class EnemyPlayAnimationAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    // AnimatorController의 Trigger 파라미터 이름.
    [SerializeReference] public BlackboardVariable<string> Trigger;

    // 참이면 재생이 끝날 때까지 Running을 유지한다.
    [SerializeReference] public BlackboardVariable<bool> WaitForEnd;

    // 재생 종료를 어느 레이어에서 판정할지. 기본 0(전신).
    // 상체 마스크 레이어에서 도는 클립(가드 / 봉인 / 소환)은 그 레이어 번호를 넣어야 한다 —
    // 0을 보면 루프 중인 걷기의 normalizedTime을 읽어 즉시 성공으로 빠져나간다.
    // WaitForEnd가 거짓이면 쓰이지 않는다.
    [SerializeReference] public BlackboardVariable<int> Layer;

    // 대기 상한(초). 0 이하면 상한 없음. 트리거 이름 오타로 패턴이 영구 정지하는 것을 막는다.
    [SerializeReference] public BlackboardVariable<float> MaxWaitSeconds;

    private EnemyAgent agent;
    private bool transitionSeen;
    private float elapsed;

    protected override Status OnStart()
    {
        agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Enemy Play Animation: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        string trigger = Trigger != null ? Trigger.Value : null;

        if (!agent.TryPlayAnimation(trigger))
        {
            LogFailure($"Enemy Play Animation: 트리거 '{trigger}'를 발동할 수 없습니다. " +
                "Animator가 없거나 트리거 이름이 비어 있습니다.");
            return Status.Failure;
        }

        bool wait = WaitForEnd != null && WaitForEnd.Value;

        if (!wait)
        {
            return Status.Success;
        }

        // Animator가 없으면 애초에 TryPlayAnimation이 실패하므로 여기 도달하지 않는다.
        transitionSeen = false;
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

        float maxWait = MaxWaitSeconds != null ? MaxWaitSeconds.Value : 0f;

        if (maxWait > 0f && elapsed >= maxWait)
        {
            Debug.LogWarning($"[{agent.name}] 애니메이션 '{(Trigger != null ? Trigger.Value : null)}' 대기가 " +
                $"{maxWait}초를 넘겨 강제 종료합니다. 트리거 이름이 AnimatorController에 있는지 확인하세요.", agent);

            return Status.Success;
        }

        int layer = Layer != null ? Layer.Value : 0;

        // 전이가 시작됐다가 끝나는 것을 본 뒤부터 진행도를 신뢰한다.
        if (agent.GetIsAnimatorInTransition(layer))
        {
            transitionSeen = true;
            return Status.Running;
        }

        if (!transitionSeen)
        {
            return Status.Running;
        }

        return agent.GetAnimationNormalizedTime(layer) >= 1f ? Status.Success : Status.Running;
    }

    protected override void OnEnd()
    {
        // 트리거는 Animator가 소비하므로 되돌릴 상태가 없다. 참조만 놓는다.
        agent = null;
    }
}
