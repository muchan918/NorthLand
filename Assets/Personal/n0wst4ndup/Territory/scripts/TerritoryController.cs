using System;
using UnityEngine;

/// <summary>
/// 영토 확장의 씬 진입점(얇은 MonoBehaviour). 그래프 모델을 소유·구축하고,
/// 뷰/입력이 호출할 유일한 변경 진입점(<see cref="TryClaim"/>)을 제공한다 — ManagementController와 동일 계보.<br/>
/// 후속 단계에서 이 진입점에 확장 정책(낮/밤 게이팅·비용)과 효과 적용이 끼워진다:
/// 정책.CanClaim → Graph.TryClaim → 효과 Apply.
/// </summary>
public class TerritoryController : MonoBehaviour
{
    /// <summary>그래프 상태가 바뀔 때 발생(Graph.OnChanged 중계). 뷰는 이걸 구독해 다시 렌더한다.</summary>
    public event Action OnChanged;

    /// <summary>읽기 질의용 모델. 상태 변경은 반드시 <see cref="TryClaim"/>으로만.</summary>
    public TerritoryGraph Graph { get; private set; }

    // 모델은 Awake에서 구축한다 — 뷰의 Start()가 어떤 순서로 돌든 그래프가 준비돼 있도록(ManagementController와 동일 이유).
    private void Awake()
    {
        Graph = BuildPlaceholderGraph();
        Graph.OnChanged += HandleGraphChanged;
    }

    /// <summary>노드 확보 시도 — 뷰/입력의 유일한 변경 진입점. 판정은 모델(구조)·정책(규칙, 후속)이 한다.</summary>
    public bool TryClaim(int nodeId)
    {
        if (Graph == null)
        {
            Debug.LogError("[영토] 그래프가 준비되지 않아 확보할 수 없습니다.");
            return false;
        }

        return Graph.TryClaim(nodeId);
    }

    private void HandleGraphChanged() => OnChanged?.Invoke();

    // TODO(#18): 런 시작 시 절차 생성(Delaunay+프루닝 생성기)으로 교체 예정 — TerritoryGraph.md §4.1.
    // 지금은 모델·뷰·입력 경로 검증용 고정 7노드 그래프. 사이클을 2개 심어
    // "미리 깔린 엣지가 확장으로 드러나는" 공개 규칙(§4.2)을 눈으로 확인할 수 있게 했다.
    //
    //        5 ─── 4
    //       /     / \
    //      1 ─── 3   (사이클: 0-1-3-2-0 / 1-3-4-5-1 / 2-3-6-2)
    //     /     / \
    //    0(본진)   6
    //     \     \ /
    //      2 ────┘
    private TerritoryGraph BuildPlaceholderGraph()
    {
        var nodes = new[]
        {
            new TerritoryNode(0, new Vector3(0f, 0f, 0f)), // 본진
            new TerritoryNode(1, new Vector3(3f, 0f, 3f)),
            new TerritoryNode(2, new Vector3(3f, 0f, -3f)),
            new TerritoryNode(3, new Vector3(6f, 0f, 0f)),
            new TerritoryNode(4, new Vector3(9f, 0f, 3f)),
            new TerritoryNode(5, new Vector3(6f, 0f, 5f)),
            new TerritoryNode(6, new Vector3(9f, 0f, -3f)),
        };

        TerritoryNode.Connect(nodes[0], nodes[1]);
        TerritoryNode.Connect(nodes[0], nodes[2]);
        TerritoryNode.Connect(nodes[1], nodes[3]);
        TerritoryNode.Connect(nodes[2], nodes[3]);
        TerritoryNode.Connect(nodes[3], nodes[4]);
        TerritoryNode.Connect(nodes[1], nodes[5]);
        TerritoryNode.Connect(nodes[5], nodes[4]);
        TerritoryNode.Connect(nodes[3], nodes[6]);
        TerritoryNode.Connect(nodes[2], nodes[6]);

        return new TerritoryGraph(nodes, homeNodeId: 0);
    }
}
