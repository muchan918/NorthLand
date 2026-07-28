using NorthLand.Combat;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 예고 범위를 바닥에 표시하고 지정 시간 뒤 치운다(#234). P3 마력 봉인의 예고 구간.
//
// 기성 자산을 그대로 쓴다 — RangeCircle이 절차적 원 지오메트리를 만들고 URP PC/Mobile 양쪽에서
// 동작하는 셰이더를 쓴다(타워 사거리 표시와 같은 것).
//
// 에이전트에 부모로 붙이므로 보스가 움직이면 원도 따라간다. 봉인 범위가 시전 시점 위치에
// 고정되어야 한다면 그래프에서 이 노드 앞에 EnemyHoldPositionAction을 두면 된다.
//
// 원은 매 시전마다 만들고 종료 시 파괴한다. Mesh/Material을 런타임 생성하므로
// 남겨두면 누수가 되고, 캐시하면 EnemyAgent가 연출 상태를 들게 된다(무상태 원칙 위반).
// P3는 쿨다운이 있어 생성 빈도가 낮다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Show Telegraph Circle",
    description: "예고 범위 원을 표시하고 지정 시간 뒤 치운다. 종료 시 정리한다.",
    story: "[Agent] telegraphs a circle of [Radius] for [Duration] seconds",
    category: "Action/Enemy",
    id: "bedf7a57996a4f35b6a4318b5488b54f")]
public partial class EnemyShowTelegraphCircleAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    [SerializeReference] public BlackboardVariable<float> Radius;

    // 표시 시간(초). 0 이하면 표시하지 않고 즉시 성공한다.
    [SerializeReference] public BlackboardVariable<float> Duration;

    [SerializeReference] public BlackboardVariable<Color> FillColor;

    [SerializeReference] public BlackboardVariable<Color> OutlineColor;

    private RangeCircle circle;
    private float elapsed;

    protected override Status OnStart()
    {
        EnemyAgent agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Enemy Show Telegraph Circle: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        float duration = Duration != null ? Duration.Value : 0f;
        float radius = Radius != null ? Radius.Value : 0f;

        if (duration <= 0f || radius <= 0f)
        {
            return Status.Success;
        }

        Color fill = FillColor != null ? FillColor.Value : new Color(1f, 0.2f, 0.2f, 0.15f);
        Color outline = OutlineColor != null ? OutlineColor.Value : new Color(1f, 0.2f, 0.2f, 0.9f);

        circle = RangeCircle.Create(agent.transform, fill, outline, "EnemyTelegraphCircle");
        circle.SetRadius(radius);
        circle.Show();

        elapsed = 0f;

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        elapsed += Time.deltaTime;

        float duration = Duration != null ? Duration.Value : 0f;

        return elapsed >= duration ? Status.Success : Status.Running;
    }

    // 정상 종료와 중단 모두 이 경로를 지난다 — 연출 오브젝트를 남기지 않는다.
    protected override void OnEnd()
    {
        if (circle != null)
        {
            Object.Destroy(circle.gameObject);
            circle = null;
        }
    }
}
