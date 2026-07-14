using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 영토 그래프의 뷰 — 렌더만 담당하고 로직이 없다(ManagementPanelView 계보).<br/>
/// 노드는 <see cref="TerritoryNodeView"/> 프리팹 인스턴스, 엣지는 LineRenderer.
/// 컨트롤러 OnChanged를 구독해 갱신하며, 점진 공개 규칙(TerritoryGraph.md §4.2)을 구현한다:<br/>
/// - Locked 노드는 완전 숨김(노드 뷰가 처리)<br/>
/// - 엣지는 <b>양끝이 모두 공개(Owned/Selectable)일 때만</b> 표시 — 프론티어에서 미공개로 뻗는 엣지가
///   보이면 숨김 정보가 누설되기 때문.<br/>
/// 씬 뷰 Gizmo로는 Locked 포함 전체 구조를 반투명으로 그려 개발 중 확인을 돕는다.
/// </summary>
public class TerritoryGraphView : MonoBehaviour
{
    [Tooltip("영토 컨트롤러(모델 소유자)")]
    [SerializeField] TerritoryController _controller;

    [Tooltip("노드 1개를 그릴 프리팹 (루트에 TerritoryNodeView + Collider)")]
    [SerializeField] TerritoryNodeView _nodePrefab;

    [Tooltip("엣지 선 머티리얼. 비우면 Sprites/Default로 대체.")]
    [SerializeField] Material _edgeMaterial;

    [Tooltip("엣지 선 굵기")]
    [SerializeField] float _edgeWidth = 0.12f;

    [Tooltip("엣지 선 색")]
    [SerializeField] Color _edgeColor = new(0.75f, 0.75f, 0.75f, 1f);

    private struct EdgeView
    {
        public int A;
        public int B;
        public GameObject Go;
    }

    private readonly List<TerritoryNodeView> _nodeViews = new();
    private readonly List<EdgeView> _edgeViews = new();

    private void Start()
    {
        if (_controller == null || _nodePrefab == null)
        {
            Debug.LogError("[영토] 뷰에 컨트롤러/노드 프리팹이 할당되지 않았습니다.", this);
            enabled = false;
            return;
        }

        if (_controller.Graph == null)
        {
            Debug.LogError("[영토] 컨트롤러에 그래프가 없습니다.", this);
            enabled = false;
            return;
        }

        BuildNodeViews();
        BuildEdgeViews();

        _controller.OnChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (_controller != null)
        {
            _controller.OnChanged -= Refresh;
        }
    }

    private void BuildNodeViews()
    {
        var nodes = _controller.Graph.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            var view = Instantiate(_nodePrefab, nodes[i].Position, Quaternion.identity, transform);
            view.name = $"Node_{nodes[i].Id}";
            view.Bind(_controller, nodes[i].Id);
            _nodeViews.Add(view);
        }
    }

    private void BuildEdgeViews()
    {
        var nodes = _controller.Graph.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var neighborIds = node.NeighborIds;
            for (int n = 0; n < neighborIds.Count; n++)
            {
                // 무방향 엣지 중복 제거: 작은 Id 쪽에서만 만든다.
                if (neighborIds[n] <= node.Id)
                {
                    continue;
                }

                var other = _controller.Graph.GetNode(neighborIds[n]);
                if (other == null)
                {
                    continue; // 모델 생성자가 이미 LogError를 남긴 케이스
                }

                _edgeViews.Add(CreateEdge(node, other));
            }
        }
    }

    private EdgeView CreateEdge(TerritoryNode a, TerritoryNode b)
    {
        var go = new GameObject($"Edge_{a.Id}_{b.Id}");
        go.transform.SetParent(transform, false);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, a.Position);
        line.SetPosition(1, b.Position);
        line.startWidth = _edgeWidth;
        line.endWidth = _edgeWidth;
        line.material = _edgeMaterial != null ? _edgeMaterial : new Material(Shader.Find("Sprites/Default"));
        line.startColor = _edgeColor;
        line.endColor = _edgeColor;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        return new() { A = a.Id, B = b.Id, Go = go };
    }

    /// <summary>모델 상태를 화면에 반영한다. 뷰는 판단하지 않고 모델 질의 결과만 그린다.</summary>
    private void Refresh()
    {
        for (int i = 0; i < _nodeViews.Count; i++)
        {
            _nodeViews[i].Refresh();
        }

        var graph = _controller.Graph;
        for (int i = 0; i < _edgeViews.Count; i++)
        {
            var edge = _edgeViews[i];
            edge.Go.SetActive(graph.IsRevealed(edge.A) && graph.IsRevealed(edge.B));
        }
    }

    // 씬 뷰 전용: Locked 포함 전체 그래프 구조를 반투명으로 표시(게임 화면은 스펙대로 숨김 유지).
    private void OnDrawGizmos()
    {
        var controller = _controller;
        if (controller == null || controller.Graph == null)
        {
            return;
        }

        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        var nodes = _controller.Graph.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            Gizmos.DrawWireSphere(nodes[i].Position, 0.55f);

            var neighborIds = nodes[i].NeighborIds;
            for (int n = 0; n < neighborIds.Count; n++)
            {
                if (neighborIds[n] <= nodes[i].Id)
                {
                    continue;
                }

                var other = _controller.Graph.GetNode(neighborIds[n]);
                if (other != null)
                {
                    Gizmos.DrawLine(nodes[i].Position, other.Position);
                }
            }
        }
    }
}
