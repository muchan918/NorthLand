using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

namespace NorthLand.Combat.Boss
{
    // 중간보스 BehaviorTree 액션 노드(#193): 자신을 MaxHp의 지정 비율(%)만큼 회복한다.
    // "HP 30% 이하일 때 회복" 패턴의 회복 실행부. 조건(BossHpBelowCondition)과 조합해 사용한다.
    // 즉시 완료되는 1회성 액션이라 OnStart에서 처리하고 Success를 반환한다.
    [System.Serializable, GeneratePropertyBag]
    [NodeDescription(
        name: "Boss Heal Self",
        description: "보스 자신의 체력을 MaxHp의 지정 비율(%)만큼 회복한다.",
        story: "Boss heal self by [Percent] percent of max HP",
        category: "Action/Boss",
        id: "bb1ea7d40a2444fa801178d7e8dab78a")]
    public partial class BossHealSelfAction : Action
    {
        // MaxHp 대비 회복 비율(퍼센트). 예: 20 = MaxHp의 20% 회복.
        [SerializeReference] public BlackboardVariable<float> Percent;

        protected override Status OnStart()
        {
            Enemy enemy = GameObject != null ? GameObject.GetComponentInParent<Enemy>() : null;
            if (enemy == null)
            {
                LogFailure("Boss Heal Self: Enemy 컴포넌트를 찾을 수 없습니다.");
                return Status.Failure;
            }

            enemy.Heal(enemy.MaxHp * (Percent.Value / 100f));
            return Status.Success;
        }
    }
}
