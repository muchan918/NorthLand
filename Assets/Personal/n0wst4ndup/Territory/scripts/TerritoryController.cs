using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 영토 확장의 씬 진입점(얇은 MonoBehaviour). 그래프 모델을 소유·구축하고,
/// 뷰/입력이 호출할 유일한 변경 진입점(<see cref="TryClaim"/>)을 제공한다 — ManagementController와 동일 계보.<br/>
/// 후속 단계에서 이 진입점에 확장 정책(낮/밤 게이팅·비용)과 효과 적용이 끼워진다:
/// 정책.CanClaim → Graph.TryClaim → 효과 Apply.
/// </summary>
public class TerritoryController : MonoBehaviour
{
    [Tooltip("절차 생성 파라미터 (TerritoryGraph.md §4.1)")]
    [SerializeField] TerritoryGraphGenSettings _settings = new();

    [Tooltip("생성 시드. 0이면 매 플레이 랜덤 — 사용된 시드를 로그로 남겨 재현 가능하게 한다")]
    [SerializeField] int _seed = 0;

    /// <summary>그래프 상태가 바뀔 때 발생(Graph.OnChanged 중계). 뷰는 이걸 구독해 다시 렌더한다.</summary>
    public event Action OnChanged;

    /// <summary>읽기 질의용 모델. 상태 변경은 반드시 <see cref="TryClaim"/>으로만.</summary>
    public TerritoryGraph Graph { get; private set; }

    // 모델은 Awake에서 구축한다 — 뷰의 Start()가 어떤 순서로 돌든 그래프가 준비돼 있도록(ManagementController와 동일 이유).
    private void Awake()
    {
        // 시드 0 = 랜덤 런. 이때도 실제 사용한 시드를 로그로 남긴다 — 버그 재현 시 인스펙터에 넣으면 같은 지형(WL-008).
        int seed = _seed != 0 ? _seed : new System.Random().Next(1, int.MaxValue);
        Debug.Log($"[영토] 그래프 생성 seed={seed} (재현: 컨트롤러 _seed에 입력)");

        Graph = BuildGeneratedGraph(_settings, seed);
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

    // 생성 파이프라인(형태) → 모델(상태) 조립. 생성기는 위치·엣지만 알고 모델을 모르므로
    // 여기서 Vector2(2D) → XZ 평면 Vector3 변환과 노드 연결을 수행한다.
    // TODO(#18): 다음 단계에서 산포와 삼각분할 사이에 프루닝(스패닝 트리 보존 + 일부 엣지만 유지)이
    //            들어간다 — 지금은 Delaunay 전체 엣지를 그대로 사용해 삼각망이 빽빽하게 보이는 게 정상.
    private static TerritoryGraph BuildGeneratedGraph(TerritoryGraphGenSettings settings, int seed)
    {
        var rng = new System.Random(seed);
        List<Vector2> positions = TerritoryGraphGenerator.ScatterPositions(settings, rng);
        List<TerritoryEdge> edges = TerritoryGraphGenerator.Triangulate(positions);

        var nodes = new TerritoryNode[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            nodes[i] = new TerritoryNode(i, new Vector3(positions[i].x, 0f, positions[i].y));
        }

        for (int i = 0; i < edges.Count; i++)
        {
            TerritoryNode.Connect(nodes[edges[i].A], nodes[edges[i].B]);
        }

        return new TerritoryGraph(nodes, homeNodeId: 0);
    }
}
