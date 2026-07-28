using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 패턴 사용 시각을 기록한다(#234). EnemyPatternGateCondition과 짝을 이룬다.
//
// 시퀀스에서 게이트 조건 바로 뒤가 아니라 실제 패턴 동작 앞에 두는 것을 권장한다 —
// 패턴이 시작됐다는 사실을 남기는 것이 목적이고, 패턴 도중 중단돼도 쿨다운은 소모된 것으로 본다.
// 중단 시 기록을 되돌리지 않는 이유: 되돌리면 중단이 반복될 때 패턴이 매 틱 재시도된다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Mark Pattern Used",
    description: "패턴 Key의 사용 시각을 기록한다. Enemy Pattern Gate 조건과 짝을 이룬다.",
    story: "[Agent] marks pattern [Key] as used",
    category: "Action/Enemy",
    id: "33606a5737e44535b78c65df88e052de")]
public partial class EnemyMarkPatternUsedAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    // 패턴 식별자. EnemyPatternGateCondition의 Key와 같아야 한다.
    [SerializeReference] public BlackboardVariable<string> Key;

    protected override Status OnStart()
    {
        EnemyAgent agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Enemy Mark Pattern Used: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        string key = Key != null ? Key.Value : null;

        if (string.IsNullOrEmpty(key))
        {
            LogFailure("Enemy Mark Pattern Used: Key가 비어 있어 게이트가 동작하지 않습니다.");
            return Status.Failure;
        }

        agent.MarkPatternUsed(key);
        return Status.Success;
    }
}
