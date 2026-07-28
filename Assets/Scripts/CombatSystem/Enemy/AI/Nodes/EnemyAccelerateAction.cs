using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 대상에 도달할 때까지 패턴 이동속도 배수를 매 프레임 끌어올린다(#234). P1 돌진의 가속 구간.
//
// 이동 소유권을 잡고 실행한다 — 잡지 않으면 본진 사거리에 진입하는 순간 Enemy.Update가
// 정지시켜 돌진이 충돌 전에 멈춘다. 경로를 벗어나지는 않는다. 속도만 올리고 이동 자체는
// MonsterMove의 웨이포인트 추종에 맡긴다.
//
// 배수를 올려도 감속 디버프 축은 그대로 곱해지므로, 감속 타워가 깔린 구간에서는
// 실효 속도가 낮게 유지되고 뒤이은 충돌 피해가 그만큼 줄어든다(P1 파훼).
//
// 원복 규칙이 비대칭이다. 소유권은 항상 반납하지만, 속도 배수는 **도달하지 못하고 끝난 경우에만**
// 되돌린다. 도달 성공 시 배수를 유지하는 이유: 바로 뒤에 오는 EnemyImpactTargetAction이
// "충돌 시점의 실효 이동속도"를 읽어 피해를 계산하는데, 여기서 원복하면 평상시 속도가 읽혀
// 감속 파훼가 무의미해진다. 성공 후 배수 1 복귀는 그래프의 기본 진군 브랜치가 담당한다
// (Docs/Monster/Boss/BossDesign.md 「BT 그래프 구조」).
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Accelerate",
    description: "대상에 도달할 때까지 패턴 이동속도 배수를 상한까지 끌어올린다. 종료 시 원복한다.",
    story: "[Agent] accelerates to [MaxFactor] toward [Target] until within [ArriveDistance]",
    category: "Action/Enemy",
    id: "6fec2b0eb53f4e8e94c2aa5d0d72c40a")]
public partial class EnemyAccelerateAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    [SerializeReference] public BlackboardVariable<GameObject> Target;

    // 배수 상한. 이 값 이상으로는 올라가지 않는다.
    [SerializeReference] public BlackboardVariable<float> MaxFactor;

    // 초당 배수 증가량.
    [SerializeReference] public BlackboardVariable<float> AccelPerSecond;

    // 대상까지 이 거리 이하가 되면 성공.
    [SerializeReference] public BlackboardVariable<float> ArriveDistance;

    // 돌진 상한(초). 0 이하면 상한 없음.
    //
    // 이게 필요한 이유는 BT의 실행 모델이다. Selector는 현재 자식이 Running인 동안
    // 앞선 자식을 다시 평가하지 않는다(SelectorComposite.OnUpdate가 Waiting을 반환).
    // 따라서 이 노드가 도달하지 못해 계속 Running이면 패턴 Selector 전체가 여기 잔류해
    // P2·P3가 영구히 발동하지 않는다. P4는 별도 병렬 브랜치라 계속 돌기 때문에
    // 겉보기로는 보스가 정상 동작하는 것처럼 보여 원인을 찾기 어렵다.
    //
    // 도달 실패 경로가 실재한다: ArriveDistance가 너무 작아 한 프레임에 지나쳐 버리거나,
    // 감속이 심해 상한 시간 안에 못 닿거나, 대상이 경로 밖에 있어 애초에 닿을 수 없는 경우.
    // 내장 Time Out 데코레이터로 감싸도 되지만, 감싸는 것을 잊으면 위 증상이 나므로
    // 노드가 스스로 방어한다(EnemyPlayAnimationAction의 MaxWaitSeconds와 같은 성격).
    [SerializeReference] public BlackboardVariable<float> MaxDuration;

    private EnemyAgent agent;
    private float previousFactor;
    private bool engaged;
    private float elapsed;

    // 도달 성공으로 끝났는지. OnEnd는 종료 사유를 받지 못하므로 여기에 남긴다.
    private bool arrived;

    protected override Status OnStart()
    {
        agent = Agent?.Value;
        engaged = false;
        arrived = false;
        elapsed = 0f;

        if (agent == null)
        {
            LogFailure("Enemy Accelerate: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        if (Target?.Value == null)
        {
            LogFailure("Enemy Accelerate: Target이 지정되지 않아 도달 판정을 할 수 없습니다.");
            return Status.Failure;
        }

        previousFactor = agent.PatternSpeedFactor;
        agent.MovementOwned = true;
        agent.MovementStopped = false;
        engaged = true;

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null)
        {
            return Status.Failure;
        }

        GameObject target = Target?.Value;

        if (target == null)
        {
            // 돌진 중 본진이 파괴된 경우 등. 가속을 멈추고 상위가 다음 판단을 하게 한다.
            return Status.Failure;
        }

        elapsed += Time.deltaTime;

        float maxDuration = MaxDuration != null ? MaxDuration.Value : 0f;

        if (maxDuration > 0f && elapsed >= maxDuration)
        {
            // 실패로 반환한다 — 성공으로 두면 뒤이은 충돌 피해가 도달하지도 않은 채 터진다.
            // 실패면 arrived가 false로 남아 OnEnd가 가속을 되돌리고, 상위 Selector는
            // 다음 틱에 조건을 다시 평가한다.
            Debug.LogWarning($"[{agent.name}] 돌진이 {maxDuration}초 안에 대상에 도달하지 못해 중단합니다. " +
                "ArriveDistance가 너무 작거나 대상이 경로에서 닿을 수 없는 위치인지 확인하세요.", agent);

            return Status.Failure;
        }

        float maxFactor = MaxFactor != null ? MaxFactor.Value : 1f;
        float accel = AccelPerSecond != null ? AccelPerSecond.Value : 0f;

        agent.PatternSpeedFactor = Mathf.Min(
            maxFactor,
            agent.PatternSpeedFactor + accel * Time.deltaTime);

        // 정지가 다른 경로에서 켜졌더라도 돌진 중에는 계속 전진해야 한다.
        agent.MovementStopped = false;

        float arriveDistance = ArriveDistance != null ? ArriveDistance.Value : 0f;
        float sqrDistance = (target.transform.position - agent.transform.position).sqrMagnitude;

        if (sqrDistance > arriveDistance * arriveDistance)
        {
            return Status.Running;
        }

        arrived = true;
        return Status.Success;
    }

    protected override void OnEnd()
    {
        if (engaged && agent != null)
        {
            // 도달하지 못한 채 끝났으면(중단·대상 소실) 가속을 되돌린다.
            // 도달했으면 충돌 피해 계산이 실효 속도를 읽을 수 있게 유지한다(상단 주석 참조).
            if (!arrived)
            {
                agent.PatternSpeedFactor = previousFactor;
            }

            agent.MovementOwned = false;
        }

        engaged = false;
        agent = null;
    }
}
