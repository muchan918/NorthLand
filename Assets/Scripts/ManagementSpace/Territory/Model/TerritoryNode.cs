using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 영토 그래프의 노드 하나 — 순수 C# 런타임 데이터.<br/>
/// "인접"은 그리드 좌표가 아니라 <see cref="NeighborIds"/>(그래프 엣지)로만 정의된다
/// (Docs/ManagementArea/TerritoryGraph.md §2 원칙 1 — 전투 공간 그리드와의 결정적 차이).<br/>
/// 위치는 free-form 데이터일 뿐(원칙 3) 배치·연출은 뷰의 몫이며,
/// 상태 전이는 <see cref="TerritoryGraph"/>가 전담한다.
/// </summary>
public class TerritoryNode
{
    public int Id { get; }

    /// <summary>free-form 월드 위치. 그리드 스냅·셀 점유 개념 없음.</summary>
    public Vector3 Position { get; }

    /// <summary>현재 상태. 전이는 <see cref="TerritoryGraph.TryClaim"/>만 수행한다.</summary>
    public TerritoryState State { get; internal set; } = TerritoryState.Locked;

    /// <summary>
    /// 이 노드에 주입된 미개척 영지 정의(그래프 조립 단계에서 정의 풀에서 랜덤 배정된다, #166).<br/>
    /// 본진·풀 미할당 시 null일 수 있다. 확보(Owned) 후 매일 정산 시 <see cref="Definition"/>.Kind 자원을
    /// <see cref="DailyYield"/>만큼 지급하는 데이터 출처가 되며, 배정은 <see cref="TerritoryController"/>가 한다.
    /// </summary>
    public TerritoryDefinition Definition { get; internal set; }

    /// <summary>
    /// 이 영지가 매일 수급하는 자원량 — <b>주입 시점에 <see cref="TerritoryDefinition"/>의 [Min,Max]에서 1회 롤해 확정</b>(#166).<br/>
    /// 노드별로 다르지만 이후 고정된다. Definition이 null(본진 등)이면 0.
    /// </summary>
    public int DailyYield { get; internal set; }

    private readonly List<int> _neighborIds = new();

    /// <summary>무방향 엣지로 연결된 이웃 노드 Id 목록.</summary>
    public IReadOnlyList<int> NeighborIds => _neighborIds;

    public TerritoryNode(int id, Vector3 position)
    {
        Id = id;
        Position = position;
    }

    /// <summary>
    /// 두 노드를 무방향 엣지로 연결한다(그래프 조립 단계 전용).<br/>
    /// 양방향을 항상 함께 기록해 이웃 목록의 비대칭 버그를 원천 차단한다. 중복 연결은 무시.
    /// </summary>
    public static void Connect(TerritoryNode a, TerritoryNode b)
    {
        if (a == null || b == null)
        {
            Debug.LogError("[영토] null 노드는 연결할 수 없습니다.");
            return;
        }

        if (a.Id == b.Id)
        {
            Debug.LogError($"[영토] 노드를 자기 자신과 연결할 수 없습니다: {a.Id}");
            return;
        }

        a.AddNeighbor(b.Id);
        b.AddNeighbor(a.Id);
    }

    private void AddNeighbor(int neighborId)
    {
        if (!_neighborIds.Contains(neighborId))
        {
            _neighborIds.Add(neighborId);
        }
    }
}
