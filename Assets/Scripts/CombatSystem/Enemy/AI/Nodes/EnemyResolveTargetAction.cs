using NorthLand.Combat;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 대상을 찾아 Blackboard 변수에 기록한다(#234). P1 돌진이 본진을 잡는 데 쓴다.
//
// 노드가 대상을 탐색하지 않는다는 원칙의 예외 지점이다. 다른 노드는 Target을 주입받지만
// 그 Target을 누군가는 한 번 채워야 하고, 본진은 밤에 런타임 스폰되므로 그래프 에셋이나
// 프리팹 인스펙터에 미리 배선할 수 없다. 그래서 "찾는 일"을 이 노드 하나로 격리한다.
//
// PlayerBase는 씬 싱글톤(PlayerBase.Instance)을 쓴다 — 기존 공개 API이며
// 성문이 런타임 스폰돼도 UI/전투가 한 경로로 참조하도록 이미 마련된 진입점이다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Resolve Target",
    description: "종류에 해당하는 대상을 찾아 Blackboard 변수에 기록한다. 못 찾으면 실패.",
    story: "[Agent] resolves [TargetKind] into [Target]",
    category: "Action/Enemy",
    id: "8540b51758274e98babe4baf55121cc3")]
public partial class EnemyResolveTargetAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    [SerializeReference] public BlackboardVariable<EnemyTargetKind> TargetKind;

    // NearestTower / NearestAlly 탐색 반경. PlayerBase와 Self는 무시한다.
    [SerializeReference] public BlackboardVariable<float> SearchRadius;

    // 결과 출력. 다른 노드가 Target 입력으로 이 변수를 받는다.
    [SerializeReference] public BlackboardVariable<GameObject> Target;

    protected override Status OnStart()
    {
        EnemyAgent agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Enemy Resolve Target: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        if (Target == null)
        {
            LogFailure("Enemy Resolve Target: 결과를 기록할 Target 변수가 지정되지 않았습니다.");
            return Status.Failure;
        }

        EnemyTargetKind kind = TargetKind != null ? TargetKind.Value : EnemyTargetKind.PlayerBase;
        GameObject resolved = Resolve(agent, kind);

        if (resolved == null)
        {
            // 본진이 아직 스폰되지 않은 경우가 정상 경로에 포함되므로 실패를 로그로 남기지 않는다.
            // 상위 Selector가 다음 브랜치로 내려가면 된다.
            return Status.Failure;
        }

        Target.Value = resolved;
        return Status.Success;
    }

    private GameObject Resolve(EnemyAgent agent, EnemyTargetKind kind)
    {
        float radius = SearchRadius != null ? SearchRadius.Value : 0f;

        switch (kind)
        {
            case EnemyTargetKind.PlayerBase:
                return PlayerBase.Instance != null ? PlayerBase.Instance.gameObject : null;

            case EnemyTargetKind.NearestTower:
                return EnemyNodeQuery.FindNearest(
                    agent, EnemyUnitFilter.Tower, EnemyRelativeDirection.Any, radius);

            case EnemyTargetKind.NearestAlly:
                return EnemyNodeQuery.FindNearest(
                    agent, EnemyUnitFilter.Ally, EnemyRelativeDirection.Any, radius);

            case EnemyTargetKind.Self:
                return agent.gameObject;

            default:
                return null;
        }
    }
}
