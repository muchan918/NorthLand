using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

namespace NorthLand.Combat.Boss
{
    // 중간보스 BehaviorTree 액션 노드(#193): 이동속도 배수를 설정한다(가감속 패턴).
    // 기준 이동속도(Stat.MoveSpeed) × Multiplier로 반영된다. 1=기본, 2=2배 빠름, 0.5=절반 느림.
    // "이동속도 빨라졌다 느려졌다" 패턴을 배수 값만 바꿔가며 시퀀스로 구성한다.
    [System.Serializable, GeneratePropertyBag]
    [NodeDescription(
        name: "Boss Set Speed Multiplier",
        description: "보스의 이동속도 배수를 설정한다. 기준 이동속도 × 배수로 반영된다.",
        story: "Boss set speed multiplier to [Multiplier]",
        category: "Action/Boss",
        id: "922a4aa2a38748b299a385ae85528ee2")]
    public partial class BossSetSpeedMultiplierAction : Action
    {
        // 기준 이동속도에 곱할 배수(음수는 0으로 클램프됨).
        [SerializeReference] public BlackboardVariable<float> Multiplier;

        protected override Status OnStart()
        {
            Enemy enemy = GameObject != null ? GameObject.GetComponentInParent<Enemy>() : null;
            if (enemy == null)
            {
                LogFailure("Boss Set Speed Multiplier: Enemy 컴포넌트를 찾을 수 없습니다.");
                return Status.Failure;
            }

            enemy.SetSpeedMultiplier(Multiplier.Value);
            return Status.Success;
        }
    }
}
