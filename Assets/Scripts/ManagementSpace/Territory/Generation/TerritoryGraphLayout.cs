using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 생성 결과 DTO — 그래프의 "형태"(위치·엣지)만 담는다.<br/>
/// 상태(Owned 등)·영토 정의(SO)·뷰는 전부 이 타입 밖의 책임이다 — 생성기는 모델을 모른다.<br/>
/// 규약: index == 노드 Id, 본진은 항상 0(원점), 위치는 2D(Vector2)이며 월드 XZ 평면 변환은 조립자의 몫.
/// </summary>
public class TerritoryGraphLayout
{
    public IReadOnlyList<Vector2> Positions { get; }
    public IReadOnlyList<TerritoryEdge> Edges { get; }
    public int HomeNodeId => 0;

    public TerritoryGraphLayout(IReadOnlyList<Vector2> positions, IReadOnlyList<TerritoryEdge> edges)
    {
        Positions = positions;
        Edges = edges;
    }
}
