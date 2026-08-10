using System.Collections.Generic;
using NorthLand.Combat;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 반경 안의 타워를 약화시킨다(#234). P3 마력 봉인의 본체.
//
// 신규 런타임 훅이 필요 없다 — Tower.ApplyBuff가 소스별 합산 구조로 열려 있어
// 배율 1 미만을 넘기면 그대로 디버프가 된다. 타워를 파괴하지 않는다.
//
// 알려진 제약 2건:
//  · 공격 간격이 Mathf.Max(finalSpeedMultiplier, 0.01f)로 클램프되어 완전 봉인은 불가능하다.
//  · 오라 계열 타워는 공격 행동이 없어 EnemyNodeQuery.IsAttackTower에서 걸러진다. 결과적으로
//    봉인 중에도 감속이 살아남아 P1 파훼 수단이 유지된다(설계 의도 — TankGraphSpec.md).
//
// 만료는 Tower가 duration으로 스스로 처리하므로 종료 시 원복하지 않는다 —
// 중단되어 상태를 남기지 않는다는 원칙은 이 노드에서 "타워가 스스로 만료시킨다"로 충족된다.
// 되돌리면 예고를 보고 회피한 플레이어가 봉인을 공짜로 벗는다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Apply Tower Debuff",
    description: "반경 안의 타워에 공격력·공격속도 배율을 걸어 약화시킨다. 파괴하지 않는다.",
    story: "[Agent] weakens towers within [Radius] for [Duration] seconds",
    category: "Action/Enemy",
    id: "aebf536f50364b9798afea7a75ac1eb9")]
public partial class EnemyApplyTowerDebuffAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    [SerializeReference] public BlackboardVariable<float> Radius;

    // 1 미만이면 공격력 감소. 예: 0.5 = 절반.
    [SerializeReference] public BlackboardVariable<float> DamageMultiplier;

    // 1 미만이면 공격속도 감소.
    [SerializeReference] public BlackboardVariable<float> AttackSpeedMultiplier;

    [SerializeReference] public BlackboardVariable<float> Duration;

    // 실제로 봉인된 타워 목록(출력). 비워두면 기록하지 않는다 — 로그만 볼 때는 연결이 필요 없다.
    //
    // 봉인 VFX를 각 타워에 걸려면 "누가 걸렸는지"가 필요하고, 그 판정은 반경·카테고리 필터를
    // 이미 통과한 이 노드만 알고 있다. 뒤에 오는 VFX 노드가 이 변수를 입력으로 받으면
    // 필터 로직을 두 곳에서 유지하지 않아도 된다.
    //
    // 매번 새 List를 할당해 대입한다. 내부 리스트를 재사용하면 소비 노드가 들고 있는 것과 같은
    // 인스턴스라, 다음 발동에서 Clear할 때 소비 측 데이터가 조용히 비워진다.
    // P3는 쿨다운(15초)이 있어 할당 빈도가 문제되지 않는다.
    [SerializeReference] public BlackboardVariable<List<GameObject>> SealedTowers;

    protected override Status OnStart()
    {
        EnemyAgent agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Enemy Apply Tower Debuff: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        float radius = Radius != null ? Radius.Value : 0f;

        if (radius <= 0f)
        {
            LogFailure("Enemy Apply Tower Debuff: Radius가 0 이하여서 대상이 없습니다.");
            return Status.Failure;
        }

        float duration = Duration != null ? Duration.Value : 0f;

        // 0 이하를 거부한다. Tower.ApplyBuff는 duration이 0 이하면 Expiry를 PositiveInfinity로
        // 저장하고(Tower.cs:232), 이 노드는 설계상 RemoveBuff를 부르지 않으므로
        // 해제 경로가 존재하지 않는 영구 타워 약화가 된다. Blackboard 변수를 연결하지 않았을 때의
        // 기본값이 하필 0이라, 막지 않으면 그래프 저작 실수 하나가 프로토타입 내내 모든 공격 타워를
        // 영구히 약화시킨다 — 로그도 없고 노드는 성공을 반환해서 원인이 보이지 않는다.
        if (duration <= 0f)
        {
            LogFailure("Enemy Apply Tower Debuff: Duration이 0 이하입니다. " +
                "이 노드는 해제 경로가 없어 영구 디버프를 걸 수 없습니다. Duration을 0보다 크게 설정하세요.");
            return Status.Failure;
        }

        // sourceId 채번: 이 에이전트 인스턴스 + 봉인이라는 효과 종류의 조합.
        // GetInstanceID만 쓰면 같은 보스가 거는 다른 효과와 충돌하고, 고정 문자열만 쓰면
        // 보스 여러 마리가 서로의 봉인을 덮어쓴다(기존 관례: 버프 타워=인스턴스별, 스킬=고정 문자열).
        int sourceId = agent.GetInstanceID() ^ SourceKindHash;

        float damageMul = DamageMultiplier != null ? DamageMultiplier.Value : 1f;
        float speedMul = AttackSpeedMultiplier != null ? AttackSpeedMultiplier.Value : 1f;

        float sqrRadius = radius * radius;
        Vector3 origin = agent.transform.position;

        List<GameObject> sealed_ = new List<GameObject>();

        // Tower.Active를 순회한다 — 물리 질의가 필요 없다.
        for (int i = 0; i < Tower.Active.Count; i++)
        {
            Tower tower = Tower.Active[i];

            // 공격 스탯이 없는 타워는 건너뛴다. 배율을 걸어도 효과가 없어 죽은 버프 엔트리만 쌓이고,
            // EnemyUnitsInRangeCondition의 Tower 필터와 대상 집합이 어긋난다.
            // 판정을 등록 여부가 아니라 카테고리로 두는 근거는 EnemyNodeQuery.IsAttackTower 주석 참조.
            if (!EnemyNodeQuery.IsAttackTower(tower))
            {
                continue;
            }

            if ((tower.transform.position - origin).sqrMagnitude > sqrRadius)
            {
                continue;
            }

            tower.ApplyBuff(sourceId, damageMul, speedMul, duration);
            sealed_.Add(tower.gameObject);
        }

        if (SealedTowers != null)
        {
            SealedTowers.Value = sealed_;
        }

        LogSealed(agent, sealed_, radius, damageMul, speedMul, duration);

        // 범위에 타워가 없는 것은 실패가 아니다 — 분산 배치라는 파훼법이 통했다는 뜻이다.
        return Status.Success;
    }

    // 프로토타입 스캐폴딩. 쿨다운(15초)마다 한 줄이라 콘솔을 잠그지 않는다.
    // 정식 빌드 전에는 걷어내거나 조건부로 바꿀 것(디버그 서클과 같은 취급).
    private static void LogSealed(EnemyAgent agent, List<GameObject> towers,
        float radius, float damageMul, float speedMul, float duration)
    {
        if (towers.Count == 0)
        {
            // 0건도 남긴다 — "발동했는데 아무것도 안 걸렸다"가 분산 배치 파훼가 통한 것인지
            // 반경·카테고리 필터가 잘못된 것인지는 로그가 없으면 구분할 수 없다.
            Debug.Log($"[보스 패턴] {Time.time:0.00}s · 마력 봉인: 반경 {radius:0.#} 안에 공격 타워 없음", agent);
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        for (int i = 0; i < towers.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            Tower tower = towers[i].GetComponent<Tower>();
            string kind = tower != null && tower.Asset != null ? tower.Asset.name : "?";
            float distance = Vector3.Distance(towers[i].transform.position, agent.transform.position);

            sb.Append($"{towers[i].name}({kind}, {distance:0.#})");
        }

        Debug.Log($"[보스 패턴] {Time.time:0.00}s · 마력 봉인 {towers.Count}기 " +
            $"— 공격력 x{damageMul:0.##} / 공격속도 x{speedMul:0.##} / {duration:0.#}초\n  {sb}", agent);
    }

    // "마력 봉인" 효과 종류를 식별하는 고정 해시. 다른 소스(버프 타워·플레이어 스킬)와 겹치지 않게
    // 이 노드 전용 문자열에서 뽑는다.
    private static readonly int SourceKindHash = "EnemyApplyTowerDebuff".GetHashCode();
}
