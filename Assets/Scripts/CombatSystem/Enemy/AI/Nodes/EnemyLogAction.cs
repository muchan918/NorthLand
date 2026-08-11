using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 콘솔에 한 줄 남기고 즉시 성공한다. 패턴이 언제 발동했는지 눈으로 확인하기 위한 스캐폴딩이다.
//
// 재생 시각(`Time.time`)을 함께 찍는다. 패턴 문제는 대부분 "발동했는가"가 아니라 "언제·몇 번
// 발동했는가"라서 — 가드가 몇 초마다 재발동하는지, 봉인 쿨다운이 도는지 — 순서만으로는 안 보인다.
//
// `Agent`는 선택 입력이다. 비어 있어도 실패하지 않는다 — 로그 노드가 시퀀스를 끊으면
// 디버깅하려던 패턴 자체가 안 돌아 목적이 뒤집힌다. 채워두면 콘솔에서 클릭했을 때 해당 보스가
// 선택된다.
//
// `Message`가 비면 아무것도 찍지 않고 성공한다. Blackboard 변수 하나로 로그를 일괄 소등할 수
// 있는 스위치다 — 디버그 서클을 `Dbg_Radius = 0`으로 끄는 것과 같은 관례
// (`TankGraphSpec.md` 「디버그 서클」).
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Log",
    description: "콘솔에 메시지를 남기고 즉시 성공한다. Message가 비면 아무것도 찍지 않는다.",
    story: "[Agent] logs [Message]",
    category: "Action/Enemy",
    id: "9d4b7f2e05a84c61b3e8d17f6a250c93")]
public partial class EnemyLogAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    [SerializeReference] public BlackboardVariable<string> Message;

    protected override Status OnStart()
    {
        string message = Message != null ? Message.Value : null;

        if (string.IsNullOrEmpty(message))
        {
            return Status.Success;
        }

        EnemyAgent agent = Agent?.Value;

        Debug.Log($"[보스 패턴] {Time.time:0.00}s · {message}", agent);

        return Status.Success;
    }
}
