using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

namespace NorthLand.Combat.Boss
{
    // 중간보스 BehaviorTree 조건 노드(#193): 현재 체력 비율이 임계값 미만인지 검사.
    // "HP 30% 이하일 때 회복" 같은 패턴의 트리거로 사용한다(Threshold=0.3).
    // 대상 Enemy는 그래프가 붙은 GameObject(=BehaviorGraphAgent의 GameObject)에서 찾는다 — 별도 배선 불필요.
    [System.Serializable, GeneratePropertyBag]
    [Condition(
        name: "Boss HP Below",
        description: "보스의 현재 체력 비율이 임계값(0~1) 미만인지 검사한다.",
        story: "Boss HP ratio below [Threshold]",
        category: "Conditions/Boss",
        id: "a86e4b599f144b1f8671ea3a24851f8d")]
    public partial class BossHpBelowCondition : Condition
    {
        // 0~1 비율. 예: 0.3 = 체력 30%.
        [SerializeReference] public BlackboardVariable<float> Threshold;

        Enemy enemy;

        public override void OnStart()
        {
            enemy = GameObject != null ? GameObject.GetComponentInParent<Enemy>() : null;
        }

        public override bool IsTrue()
        {
            return enemy != null && enemy.HpRatio < Threshold.Value;
        }
    }
}
