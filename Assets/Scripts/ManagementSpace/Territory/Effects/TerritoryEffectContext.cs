using UnityEngine;

/// <summary>
/// 영토 효과(<see cref="TerritoryEffect"/>)가 확보 시점에 건드릴 시스템을 노출하는 적용 컨텍스트(TerritoryGraph.md §5).<br/>
/// 효과 SO는 이 컨텍스트만 통해 외부에 접근하므로, 효과가 아무리 늘어도 컨텍스트가 노출하는 표면만
/// 갖춰지면 그래프/전이/뷰 코드는 불변이다(§5 "신규 효과가 생겨도 코드 불변").<br/>
/// <br/>
/// 노출 대상(§6 통합 계약):<br/>
/// - <see cref="Wallet"/>: 자원/마나석 지급의 정당한 원천(팀 계약 #3 — 마나석은 영토 확장·전투 보상에서만).<br/>
/// - <see cref="AddProductionMultiplier"/>: 패시브 생산 배율 등록(<see cref="ProductionModifiers"/>).<br/>
/// - <see cref="GainResidents"/>: 주민 획득 심 — 주민 시스템 부재로 현재 placeholder(§6.1·§9).<br/>
/// - <see cref="Node"/>/<see cref="Graph"/>: 대상 노드·그래프 참조(특수 효과가 위치/인접을 참조할 여지).<br/>
/// <br/>
/// 컨텍스트는 <see cref="ManagementController"/>가 <c>OnNodeClaimed</c> 구독 시 조립해
/// <see cref="TerritoryDefinition.ApplyAll"/>에 넘긴다(WL-030 배선 완료).
/// </summary>
public readonly struct TerritoryEffectContext
{
    /// <summary>자원/마나석 지급 대상 지갑. 지급 외 소비 규칙은 컨텍스트 밖의 책임.</summary>
    public readonly ResourceWallet Wallet;

    /// <summary>패시브 생산 배율 레지스트리.</summary>
    public readonly ProductionModifiers Production;

    /// <summary>효과가 적용되는 대상(확보된) 노드.</summary>
    public readonly TerritoryNode Node;

    /// <summary>대상 노드가 속한 그래프(인접/위치 질의용).</summary>
    public readonly TerritoryGraph Graph;

    public TerritoryEffectContext(ResourceWallet wallet, ProductionModifiers production, TerritoryNode node, TerritoryGraph graph)
    {
        Wallet = wallet;
        Production = production;
        Node = node;
        Graph = graph;
    }

    /// <summary>지정 자원의 생산 배율을 곱셈 누적한다(패시브 효과 전용). 레지스트리가 없으면 무시.</summary>
    public void AddProductionMultiplier(ResourceKind kind, float multiplier)
    {
        if (Production == null)
        {
            Debug.LogError($"[영토] 생산 배율 레지스트리가 없어 패시브({kind} ×{multiplier})를 등록할 수 없습니다.");
            return;
        }

        Production.AddMultiplier(kind, multiplier);
    }

    /// <summary>
    /// 주민 획득 심 — 주민 시스템이 없으므로 현재는 로그만 남긴다(GDD §5.1 "영토 확장으로 주민 풀 증가"는
    /// 스펙만 존재, 코드 미구현). 주민 시스템 도입 시 이 심의 구현만 교체하면 효과 SO는 그대로다.
    /// </summary>
    public void GainResidents(int count)
    {
        if (count <= 0)
        {
            return;
        }

        Debug.Log($"[영토] 주민 +{count} 획득(심) — 주민 시스템 부재로 실제 반영 보류(§6.1 placeholder).");
    }
}
