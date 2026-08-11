using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 필드의 살아있는 몬스터 수가 임계값 이하로 줄어들 때까지 Running을 유지한다.
//
// P4 지속 소환의 게이트다. 기존 P4는 보스 등장과 동시에 무조건 유입을 시작했는데, 그러면
// 플레이어가 잡몹을 정리해도 보스가 곧바로 다시 채워 넣어 "정리한다"는 행위에 보상이 없다.
// 게이트를 앞에 두면 순서가 뒤집힌다 — 필드를 비운 대가로 무한 유입이 열리고, 그때부터는
// 보스를 죽이는 것만이 유입을 멈추는 방법이 된다(P4 절의 파훼법 그대로).
//
// **Condition이 아니라 Running을 유지하는 Action이다.** Condition으로 두면 상위에 폴링 구조
// (`Repeat Until Success` + `Conditional Guard`)를 얹어야 하는데, 그 조합은 통과하는 사이클에
// 부모 시퀀스를 한 번 되감아 뒤따르는 소환 모션이 두 번 재생된다.
//
// **게이트는 한 번 열리면 닫히지 않는다.** 이 노드 뒤에 `Repeat (Forever)`를 두면 시퀀스가
// 이 노드로 되돌아오지 않으므로 구조가 래치를 보장한다 — 별도 플래그나 패턴 게이트가 필요 없다.
// 소환이 시작된 뒤에는 잡몹이 다시 늘어나도 조건을 재평가하지 않는다.
//
// 정지 배선이 없다 — 보스 사망·게임 종료 시 Enemy가 behaviorAgent.enabled = false로 그래프
// 틱을 멈추므로 대기도 소환도 함께 멈춘다(EnemySpawnMinionsAction과 같은 이유).
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Wait Until Field Cleared",
    description: "필드의 살아있는 몬스터 수가 MaxAliveCount 이하가 될 때까지 대기한다. 지속 소환의 게이트.",
    story: "[Agent] waits until the field has [MaxAliveCount] or fewer monsters",
    category: "Action/Enemy",
    id: "5c1d8ab34e7f42b0915ae6c273d84f16")]
public partial class EnemyWaitUntilFieldClearedAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    // 통과 임계값. **보스 자신과 사망 연출 중인 몬스터도 집계에 포함된다**
    // (MonsterSpawn.AliveMonsterCount — destroyDelay 2초, WL-038).
    //
    // 그래서 "필드에 보스만 남았다"는 값이 0이 아니라 1이다. 씬에 직접 배치한 테스트 보스는
    // monsterParent 자식이 아니라 집계에서 빠지므로 0이 맞다.
    //
    // 마지막 잡몹이 죽고 destroyDelay(2초)가 지난 뒤에 게이트가 열린다 — 즉시가 아니다.
    [SerializeReference] public BlackboardVariable<int> MaxAliveCount;

    private EnemyAgent agent;

    // 경고 래치. OnStart는 보통 한 번만 지나지만, 실패로 끝난 브랜치를 상위가 되감으면 반복된다.
    private bool warnedNeverOpens;

    protected override Status OnStart()
    {
        agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Enemy Wait Until Field Cleared: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        // 스포너가 없으면 AliveMonsterCount가 항상 0을 반환해 게이트가 즉시 열린다 —
        // 게이트 이전 동작(등장과 함께 무한 유입)으로 조용히 되돌아가는 셈이라 실패로 막는다.
        // 뒤따르는 EnemySpawnMinionsAction도 같은 이유로 실패를 반환한다.
        if (!agent.HasSpawner)
        {
            LogFailure("Enemy Wait Until Field Cleared: 스포너가 주입되지 않았습니다. " +
                "런타임 스폰이 아니라 씬에 직접 배치했다면 EnemyAgent의 spawner 칸을 채워야 합니다.");
            return Status.Failure;
        }

        int maxAlive = MaxAliveCount != null ? MaxAliveCount.Value : 0;

        // Blackboard 미연결 기본값이 0이고, 스폰된 보스는 자신도 집계에 들어가므로
        // 0이면 보스가 살아있는 동안 조건이 성립하지 않는다 — 게이트가 영구히 닫히고
        // 지속 소환이 한 번도 돌지 않는다. 컴파일도 통과하고 로그도 없어 밸런싱 문제로 보인다.
        if (maxAlive <= 0 && !warnedNeverOpens)
        {
            warnedNeverOpens = true;

            Debug.LogWarning($"[{agent.name}] 지속 소환 게이트의 MaxAliveCount가 {maxAlive}입니다. " +
                "스포너가 스폰한 보스는 자신도 집계에 포함되므로 이 값이 0 이하면 게이트가 열리지 않아 " +
                "소환이 시작되지 않습니다. 스폰된 보스는 1, 씬에 직접 배치한 테스트 보스는 0이 맞습니다.", agent);
        }

        // 진입 시점에 이미 조건이 성립했으면 한 프레임도 낭비하지 않는다.
        return agent.AliveMonsterCount <= maxAlive ? Status.Success : Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (agent == null)
        {
            return Status.Failure;
        }

        int maxAlive = MaxAliveCount != null ? MaxAliveCount.Value : 0;

        return agent.AliveMonsterCount <= maxAlive ? Status.Success : Status.Running;
    }

    protected override void OnEnd()
    {
        // 아무 상태도 바꾸지 않으므로 원복할 것이 없다. 참조만 놓는다.
        agent = null;
    }
}
