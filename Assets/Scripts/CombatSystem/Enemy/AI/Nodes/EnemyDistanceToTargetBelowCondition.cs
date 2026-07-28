using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 대상까지의 거리가 임계값 미만인지 검사한다(#234). P1 본진 돌진의 발동 트리거.
//
// Target이 null이면 조용히 거짓을 반환한다 — 본진(성문)은 밤에 런타임 스폰되므로
// 그래프가 돌기 시작한 초반에 null인 것이 정상이고, 로그를 남기면 매 틱 콘솔이 잠긴다.
//
// 네임스페이스를 두지 않는다(Docs/Monster/Boss/BossNodeReference.md 「작성 규약」).
[System.Serializable, GeneratePropertyBag]
[Condition(
    name: "Enemy Distance To Target Below",
    description: "대상까지의 거리가 임계값 미만이면 참. 대상이 없으면 거짓.",
    story: "[Agent] is within [Distance] of [Target]",
    category: "Conditions/Enemy",
    id: "e16b05e7c9ec4194b24362ad8bda0df1")]
public partial class EnemyDistanceToTargetBelowCondition : Condition
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    // 임계 거리(월드 단위). 이 값 미만이면 참.
    [SerializeReference] public BlackboardVariable<float> Distance;

    public override bool IsTrue()
    {
        EnemyAgent agent = Agent?.Value;
        GameObject target = Target?.Value;

        if (agent == null || target == null)
        {
            return false;
        }

        float threshold = Distance != null ? Distance.Value : 0f;

        if (threshold <= 0f)
        {
            return false;
        }

        // 제곱 비교로 sqrt를 피한다 — 조건 노드는 매 틱 평가된다.
        float sqrDistance = (target.transform.position - agent.transform.position).sqrMagnitude;

        return sqrDistance < threshold * threshold;
    }
}
