using NorthLand.Combat;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 대상에 실효 이동속도 비례 피해를 준다(#234). P1 돌진의 충돌 피해.
//
// 피해 입력이 패턴 배수가 아니라 EnemyAgent.EffectiveMoveSpeed인 것이 이 노드의 핵심이다.
// 배수를 읽으면 감속 디버프가 반영되지 않아 "이동속도 감소 타워로 돌진을 파훼한다"가
// 기능적으로 성립하지 않는다.
//
// 실효 속도가 MinSpeed 미만이면 피해 없이 성공한다 — 감속으로 충분히 늦춘 플레이어가
// 충돌을 완전히 무력화하는 보상 구간이다. 실패로 두면 상위 시퀀스가 매 틱 재시도한다.
//
// 상태를 바꾸지 않으므로 원복할 것이 없다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Impact Target",
    description: "대상에 실효 이동속도 비례 피해를 준다. 실효 속도가 하한 미만이면 피해 없이 성공.",
    story: "[Agent] impacts [Target] for [DamagePerSpeedUnit] per speed unit",
    category: "Action/Enemy",
    id: "016e665298c54a71b78d5524a2a29831")]
public partial class EnemyImpactTargetAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    [SerializeReference] public BlackboardVariable<GameObject> Target;

    // 실효 이동속도 1당 피해량. 최종 피해 = 실효 속도 × 이 값.
    [SerializeReference] public BlackboardVariable<float> DamagePerSpeedUnit;

    // 이 속도 미만이면 피해를 주지 않는다.
    [SerializeReference] public BlackboardVariable<float> MinSpeed;

    protected override Status OnStart()
    {
        EnemyAgent agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Enemy Impact Target: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        GameObject target = Target?.Value;

        if (target == null)
        {
            LogFailure("Enemy Impact Target: Target이 지정되지 않았습니다.");
            return Status.Failure;
        }

        float speed = agent.EffectiveMoveSpeed;
        float minSpeed = MinSpeed != null ? MinSpeed.Value : 0f;

        if (speed < minSpeed)
        {
            return Status.Success;
        }

        // 대상 본체가 콜라이더 자식일 수 있으므로 부모까지 올라가 계약을 찾는다.
        IDamageable damageable = target.GetComponentInParent<IDamageable>();

        if (damageable == null)
        {
            LogFailure($"Enemy Impact Target: '{target.name}'에 IDamageable이 없어 피해를 줄 수 없습니다.");
            return Status.Failure;
        }

        if (damageable.IsDead)
        {
            return Status.Success;
        }

        float damagePerUnit = DamagePerSpeedUnit != null ? DamagePerSpeedUnit.Value : 0f;

        // Source는 IAttacker 계약이며 EnemyAgent는 이를 구현하지 않는다.
        // 충돌 피해는 반격·처치 기여 집계 대상이 아니므로 null로 둔다(Enemy의 근접 공격과 구분).
        damageable.TakeDamage(new DamageInfo(speed * damagePerUnit, null));

        return Status.Success;
    }
}
