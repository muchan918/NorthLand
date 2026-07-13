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

    private readonly List<int> _neighborIds = new List<int>();

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
