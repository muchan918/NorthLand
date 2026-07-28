using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 스폰 지점에 잡몹을 투입한다(#234). P4 지속 소환의 본체.
//
// 소환체는 EnemyAgent를 거쳐 MonsterSpawn의 공개 API로 만들어지므로 monsterParent 자식으로
// 들어가고 경로를 받는다. 웨이브 클리어 판정이 monsterParent.childCount == 0이라
// 밖에 두면 보스 사망 즉시 웨이브가 종료되면서 잡몹이 남는다.
//
// 정지 배선이 없다 — 보스 사망·게임 종료 시 Enemy가 behaviorAgent.enabled = false로
// 그래프 틱을 멈추므로 소환도 함께 멈춘다.
//
// Prefab을 Blackboard 변수로 받을 수 있는지 패키지 소스에서 확인했다: GameObject가 기본
// Blackboard 타입이고 UnityEngine.Object 파생 변수는 ObjectValue로 에셋 참조를 직렬화한다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Spawn Minions",
    description: "스폰 지점에 잡몹을 투입한다. 동시 생존 수가 상한 이상이면 건너뛰고 성공한다.",
    story: "[Agent] spawns [Count] of [Prefab] up to [MaxAlive] alive",
    category: "Action/Enemy",
    id: "a8eff05a81ac41ccad9d9dc6fde2a359")]
public partial class EnemySpawnMinionsAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    // 소환할 잡몹 프리팹. Enemy와 MonsterMove가 모두 있어야 한다(없으면 스포너가 거부한다).
    [SerializeReference] public BlackboardVariable<GameObject> Prefab;

    // 이번 호출에서 투입할 수.
    [SerializeReference] public BlackboardVariable<int> Count;

    // 동시 생존 상한. 보스 자신과 사망 연출 중인 몬스터도 집계에 포함된다
    // (MonsterSpawn.AliveMonsterCount 주석 — destroyDelay 2초).
    [SerializeReference] public BlackboardVariable<int> MaxAlive;

    protected override Status OnStart()
    {
        EnemyAgent agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Enemy Spawn Minions: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        GameObject prefab = Prefab?.Value;

        if (prefab == null)
        {
            LogFailure("Enemy Spawn Minions: 소환할 Prefab이 지정되지 않았습니다.");
            return Status.Failure;
        }

        if (!agent.HasSpawner)
        {
            LogFailure("Enemy Spawn Minions: 스포너가 주입되지 않았습니다. " +
                "런타임 스폰이 아니라 씬에 직접 배치했다면 EnemyAgent의 spawner 칸을 채워야 합니다.");
            return Status.Failure;
        }

        int count = Count != null ? Count.Value : 0;

        if (count <= 0)
        {
            return Status.Success;
        }

        int maxAlive = MaxAlive != null ? MaxAlive.Value : 0;

        for (int i = 0; i < count; i++)
        {
            // 상한은 매 마리 재확인한다 — 한 번에 여러 마리를 투입할 때 상한을 넘겨버리지 않도록.
            if (maxAlive > 0 && agent.AliveMonsterCount >= maxAlive)
            {
                break;
            }

            agent.SpawnMinion(prefab);
        }

        // 상한에 걸려 한 마리도 못 넣은 경우도 성공이다 — 실패로 두면 상위 시퀀스가 매 틱 재시도한다.
        return Status.Success;
    }
}
