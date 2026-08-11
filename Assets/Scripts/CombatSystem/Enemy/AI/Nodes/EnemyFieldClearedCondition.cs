using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 필드의 살아있는 몬스터 수가 한 번이라도 MaxAliveCount 이하로 내려갔으면 참. **래치다 — 한 번 참이 되면
// 다시 거짓으로 돌아가지 않는다.**
//
// 지속 소환의 게이트를 패턴 Selector 안에서 쓰기 위한 형태다. 같은 판정을 하는
// `EnemyWaitUntilFieldClearedAction`은 조건이 성립할 때까지 Running을 유지하므로 Selector 브랜치에
// 넣으면 그 브랜치가 트리를 붙잡아 다른 패턴이 전부 멎는다. 조건으로 두면 통과하지 못한 사이클에
// 다음 브랜치로 넘어간다.
//
// **왜 래치인가.** 게이트가 열려 소환이 시작되면 잡몹 수가 다시 늘어난다. 래치가 없으면 조건이
// 곧바로 거짓이 되어 소환이 한 번만 나가고 멈춘다 — "그때부터 무한정"이라는 설계와 반대다.
// 래치는 노드 인스턴스 필드다. 그래프 인스턴스가 보스 1체에 하나씩 붙으므로 보스별로 독립이며,
// 보스가 죽으면 인스턴스와 함께 사라진다.
//
// 집계는 `MonsterSpawn.AliveMonsterCount`(= `monsterParent.childCount`)다. **보스 자신과 사망 연출
// 중인 몬스터가 포함되므로** "보스만 남았다"는 0이 아니라 1이고, 마지막 잡몹이 죽고
// `destroyDelay`(2초)가 지난 뒤에 열린다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[Condition(
    name: "Enemy Field Cleared",
    description: "필드의 살아있는 몬스터 수가 한 번이라도 MaxAliveCount 이하가 되었으면 참(래치).",
    story: "[Agent] has seen the field cleared to [MaxAliveCount] or fewer",
    category: "Conditions/Enemy",
    id: "3a9f61c07b5d4e28ac4d90f5127be6c4")]
public partial class EnemyFieldClearedCondition : Condition
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    // 게이트 통과 임계값. 스폰된 보스가 혼자 남은 상태 = 1.
    // 씬에 직접 배치한 테스트 보스는 monsterParent 자식이 아니라 집계에서 빠지므로 0.
    [SerializeReference] public BlackboardVariable<int> MaxAliveCount;

    private bool latched;
    private bool warnedNoSpawner;

    public override bool IsTrue()
    {
        if (latched)
        {
            return true;
        }

        EnemyAgent agent = Agent?.Value;

        if (agent == null)
        {
            return false;
        }

        // 스포너가 없으면 AliveMonsterCount가 항상 0을 반환해 게이트가 즉시 열린다 —
        // 게이트 이전 동작(등장과 함께 무한 유입)으로 조용히 되돌아가는 셈이라 막는다.
        // 조건 노드는 매 틱 평가되므로 경고에 래치가 필요하다.
        if (!agent.HasSpawner)
        {
            if (!warnedNoSpawner)
            {
                warnedNoSpawner = true;

                Debug.LogWarning($"[{agent.name}] 지속 소환 게이트: 스포너가 주입되지 않아 게이트를 닫아 둡니다. " +
                    "런타임 스폰이 아니라 씬에 직접 배치했다면 EnemyAgent의 spawner 칸을 채워야 합니다.", agent);
            }

            return false;
        }

        int maxAlive = MaxAliveCount != null ? MaxAliveCount.Value : 0;

        if (agent.AliveMonsterCount > maxAlive)
        {
            return false;
        }

        latched = true;
        return true;
    }
}
