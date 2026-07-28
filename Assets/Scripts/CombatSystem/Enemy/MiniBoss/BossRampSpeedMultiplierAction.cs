using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

namespace NorthLand.Combat.Boss
{
    // 중간보스 BehaviorTree 액션 노드(#193): 이동속도 배수를 현재 값에서 목표 값까지
    // 지정 시간(Duration)에 걸쳐 서서히 보간(Lerp)한다. "서서히 빨라졌다 느려졌다" 패턴용.
    // 즉시 변경은 BossSetSpeedMultiplierAction을 사용한다.
    [System.Serializable, GeneratePropertyBag]
    [NodeDescription(
        name: "Boss Ramp Speed Multiplier",
        description: "보스의 이동속도 배수를 현재 값에서 목표 값까지 지정 시간에 걸쳐 서서히 보간한다.",
        story: "Boss ramp speed multiplier to [Target] over [Duration] seconds",
        category: "Action/Boss",
        id: "0f542ebee54c481eae5dbb15b82fe643")]
    public partial class BossRampSpeedMultiplierAction : Action
    {
        // 도달할 목표 배수. 1=기본, 2=2배, 0.5=절반.
        [SerializeReference] public BlackboardVariable<float> Target;
        // 목표에 도달하기까지 걸리는 시간(초). 0 이하면 즉시 적용.
        [SerializeReference] public BlackboardVariable<float> Duration;

        Enemy enemy;
        float startMultiplier;
        float elapsed;

        protected override Status OnStart()
        {
            enemy = GameObject != null ? GameObject.GetComponentInParent<Enemy>() : null;
            if (enemy == null)
            {
                LogFailure("Boss Ramp Speed Multiplier: Enemy 컴포넌트를 찾을 수 없습니다.");
                return Status.Failure;
            }

            startMultiplier = enemy.SpeedMultiplier;
            elapsed = 0f;

            // 시간이 0 이하면 보간 없이 즉시 세팅하고 종료.
            if (Duration.Value <= 0f)
            {
                enemy.SetSpeedMultiplier(Target.Value);
                return Status.Success;
            }

            return Status.Running;
        }

        protected override Status OnUpdate()
        {
            if (enemy == null)
            {
                return Status.Failure;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Duration.Value);
            enemy.SetSpeedMultiplier(Mathf.Lerp(startMultiplier, Target.Value, t));

            return t >= 1f ? Status.Success : Status.Running;
        }
    }
}
