using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 영토 그래프의 뷰 — 렌더만 담당하고 로직이 없다(ManagementPanelView 계보).<br/>
/// 노드는 <see cref="TerritoryNodeView"/> 프리팹 인스턴스, 엣지는 그 위를 왕복하는 배(#93,
/// <see cref="TerritoryEdgeShip"/>)로 표현한다 — 선(LineRenderer)은 디버그용으로만 남겨 기본 꺼짐.
/// 컨트롤러 OnChanged를 구독해 갱신하며, 점진 공개 규칙(TerritoryGraph.md §4.2)을 구현한다:<br/>
/// - Locked 노드는 완전 숨김(노드 뷰가 처리)<br/>
/// - 엣지(=배)는 <b>양끝이 모두 확보(Owned)일 때만</b> 표시(#93) — 확보 가능(Selectable)한 프론티어로
///   뻗는 엣지는 아직 배가 다니지 않는다.<br/>
/// 씬 뷰 Gizmo로는 Locked 포함 전체 구조를 반투명으로 그려 개발 중 확인을 돕는다.
/// </summary>
public class TerritoryGraphView : MonoBehaviour
{
    [Tooltip("영토 컨트롤러(모델 소유자)")]
    [SerializeField] TerritoryController _controller;

    [Tooltip("노드 1개를 그릴 프리팹 (루트에 TerritoryNodeView + Collider)")]
    [SerializeField] TerritoryNodeView _nodePrefab;

    [Header("엣지 — 배 연출(#93)")]
    [Tooltip("엣지마다 랜덤으로 1척 뽑아 왕복시킬 배 프리팹 풀 (SweetBoat_01~05). 비우면 배 없이 선 폴백.")]
    [SerializeField] GameObject[] _shipPrefabs;

    [Tooltip("배 이동 속도 (units/sec)")]
    [SerializeField] float _shipSpeed = 4f;

    [Tooltip("배 뱃머리 방향 보정각(도). FBX forward 축에 맞춰 에디터에서 눈으로 맞춘다.")]
    [SerializeField] float _shipYawOffset = 0f;

    [Tooltip("배 높이 보정(수면 위치 미세 조정)")]
    [SerializeField] float _shipHeightOffset = 0f;

    [Tooltip("양끝을 노드 안쪽으로 당기는 거리 — 노드 메시와 겹침 방지")]
    [SerializeField] float _shipEndpointInset = 0.5f;

    [Tooltip("끝점에서 방향 반전 시 회전 속도(도/sec). 0이면 즉시 반전.")]
    [SerializeField] float _shipTurnSpeed = 180f;

    [Header("엣지 — 디버그 선")]
    [Tooltip("엣지 선(LineRenderer)을 그릴지 여부. #93부터 기본 꺼짐(배로 대체). 디버그용.")]
    [SerializeField] bool _drawEdgeLines = false;

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

    // 노드 좌표(TerritoryNode.Position)는 그래프 로컬 공간(본진=원점)이다. 이 뷰의 transform을
    // 씬에서 옮기면 그래프 전체가 그만큼 이동해 보이도록 TransformPoint로 월드 좌표를 계산한다
    // (본진 근처 배치는 뷰 오브젝트 위치만으로 결정 — 모델은 배치를 모른다, §7 로직/뷰 분리).
    private void BuildNodeViews()
    {
        var nodes = _controller.Graph.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            var worldPos = transform.TransformPoint(nodes[i].Position);
            var view = Instantiate(_nodePrefab, worldPos, transform.rotation, transform);
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

    // 엣지 1개 = 컨테이너 GameObject 1개. 그 위에 (옵션) 디버그 선과 왕복하는 배를 얹는다.
    // Refresh가 컨테이너를 SetActive로 껐다 켜므로 선/배 모두 공개 규칙을 그대로 따른다.
    private EdgeView CreateEdge(TerritoryNode a, TerritoryNode b)
    {
        var go = new GameObject($"Edge_{a.Id}_{b.Id}");
        go.transform.SetParent(transform, false);

        var endA = transform.TransformPoint(a.Position);
        var endB = transform.TransformPoint(b.Position);

        if (_drawEdgeLines)
        {
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, endA);
            line.SetPosition(1, endB);
            line.startWidth = _edgeWidth;
            line.endWidth = _edgeWidth;
            line.material = _edgeMaterial != null ? _edgeMaterial : new Material(Shader.Find("Sprites/Default"));
            line.startColor = _edgeColor;
            line.endColor = _edgeColor;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        SpawnEdgeShip(go.transform, endA, endB);

        return new() { A = a.Id, B = b.Id, Go = go };
    }

    // 엣지 컨테이너의 자식으로 랜덤 배 1척을 붙여 두 끝점 사이를 왕복시킨다(#93).
    private void SpawnEdgeShip(Transform parent, Vector3 endA, Vector3 endB)
    {
        if (_shipPrefabs == null || _shipPrefabs.Length == 0)
        {
            // 배가 없고 선도 안 그리면 엣지가 아예 안 보인다 — 배선 누락을 알린다.
            if (!_drawEdgeLines)
            {
                Debug.LogWarning("[영토] 배 프리팹(_shipPrefabs)이 비어 있어 엣지 연출이 표시되지 않습니다.", this);
            }
            return;
        }

        var prefab = _shipPrefabs[Random.Range(0, _shipPrefabs.Length)];
        if (prefab == null)
        {
            return;
        }

        var ship = Instantiate(prefab, parent);

        // 배 콜라이더가 노드 선택 레이캐스트(MouseManager)를 가로채지 않도록 제거한다.
        foreach (var col in ship.GetComponentsInChildren<Collider>(true))
        {
            Destroy(col);
        }

        var mover = ship.GetComponent<TerritoryEdgeShip>();
        if (mover == null)
        {
            mover = ship.AddComponent<TerritoryEdgeShip>();
        }
        mover.Init(endA, endB, _shipSpeed, _shipYawOffset, _shipHeightOffset, _shipEndpointInset, _shipTurnSpeed);
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
            // 배는 양끝이 모두 확보(Owned)된 엣지에서만 흐른다(#93) — 확보 가능(Selectable)한
            // 프론티어로 뻗는 엣지는 아직 배가 다니지 않는다.
            edge.Go.SetActive(graph.IsOwned(edge.A) && graph.IsOwned(edge.B));
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
            Gizmos.DrawWireSphere(transform.TransformPoint(nodes[i].Position), 0.55f);

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
                    Gizmos.DrawLine(transform.TransformPoint(nodes[i].Position), transform.TransformPoint(other.Position));
                }
            }
        }
    }
}
