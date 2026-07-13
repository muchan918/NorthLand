using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 경영 영토 확장의 런타임 모델(순수 C#, UI 무관). 노드/엣지를 소유하고 상태 전이·질의만 담당한다.<br/>
/// 렌더링·입력은 뷰의 몫, 확장 "규칙"(낮/밤 게이팅·비용)은 정책의 몫이고,
/// 이 모델은 구조 불변식 하나만 지킨다: <b>Selectable 노드만 확보할 수 있다</b>.<br/>
/// <br/>
/// 상태 전이(Docs/ManagementArea/TerritoryGraph.md §4.3):<br/>
/// 1. 초기화 — 본진 Owned, 본진 인접 Selectable, 나머지 Locked.<br/>
/// 2. 확보 — Selectable → Owned 전이 후 이웃 Locked를 Selectable로 승격(프론티어 갱신).<br/>
/// 3. 전이 시 이벤트 발행 → 뷰가 구독해 갱신(ResourceWallet과 동일한 순수 C# 모델 계보).
/// </summary>
public class TerritoryGraph
{
    private readonly Dictionary<int, TerritoryNode> _nodesById = new();
    private readonly List<TerritoryNode> _nodes = new();

    /// <summary>그래프 상태가 바뀔 때마다 발행된다. 뷰는 이걸 구독해 다시 렌더한다.</summary>
    public event Action OnChanged;

    /// <summary>노드를 확보(Owned 전이)했을 때 정확히 1회 발행된다 — 효과 1회 적용 지점(§4.3).</summary>
    public event Action<TerritoryNode> OnNodeClaimed;

    /// <summary>본진 노드 Id. 초기화 실패 시 -1.</summary>
    public int HomeNodeId { get; private set; } = -1;

    /// <summary>모든 노드(입력 순서 유지). 숨김(Locked) 노드도 포함하므로 공개 여부는 <see cref="IsRevealed"/>로 판정.</summary>
    public IReadOnlyList<TerritoryNode> Nodes => _nodes;

    public TerritoryGraph(IReadOnlyList<TerritoryNode> nodes, int homeNodeId)
    {
        if (nodes == null || nodes.Count == 0)
        {
            Debug.LogError("[영토] 노드 없이 그래프를 만들 수 없습니다.");
            return;
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node == null)
            {
                Debug.LogError($"[영토] {i}번 노드가 null이라 제외합니다.");
                continue;
            }

            if (_nodesById.ContainsKey(node.Id))
            {
                Debug.LogError($"[영토] 중복 Id 노드를 제외합니다: {node.Id}");
                continue;
            }

            node.State = TerritoryState.Locked; // 조립 중 상태가 어떻든 초기화 규칙(§4.3-1)에서 다시 시작
            _nodesById[node.Id] = node;
            _nodes.Add(node);
        }

        if (!_nodesById.TryGetValue(homeNodeId, out var home))
        {
            Debug.LogError($"[영토] 본진 노드를 찾을 수 없습니다: {homeNodeId}");
            return;
        }

        HomeNodeId = homeNodeId;
        home.State = TerritoryState.Owned;
        PromoteLockedNeighbors(home);
    }

    /// <summary>노드 질의. 없으면 null(호출부 체크 규약 — DataTableManager.Get과 동일).</summary>
    public TerritoryNode GetNode(int id) =>
        _nodesById.TryGetValue(id, out var node) ? node : null;

    /// <summary>현재 프론티어(Selectable) 노드들.</summary>
    public IEnumerable<TerritoryNode> Frontier
    {
        get
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].State == TerritoryState.Selectable)
                {
                    yield return _nodes[i];
                }
            }
        }
    }

    public int OwnedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].State == TerritoryState.Owned)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>플레이어에게 공개된 노드인가 (Owned 또는 Selectable — §4.2 점진 공개).</summary>
    public bool IsRevealed(int id)
    {
        var node = GetNode(id);
        return node != null && node.State != TerritoryState.Locked;
    }

    /// <summary>
    /// 노드 확보 시도 — 이 모델의 유일한 상태 변경 진입점.<br/>
    /// Selectable이 아니면(구조 불변식 위반) 확보하지 않고 false를 반환한다.
    /// 성공 시 이웃 Locked를 Selectable로 승격하고 OnNodeClaimed → OnChanged 순으로 발행한다.
    /// </summary>
    public bool TryClaim(int nodeId)
    {
        if (!_nodesById.TryGetValue(nodeId, out var node))
        {
            Debug.LogError($"[영토] 존재하지 않는 노드를 확보하려 했습니다: {nodeId}");
            return false;
        }

        if (node.State != TerritoryState.Selectable)
        {
            // 보유/숨김 노드 클릭은 정상 입력 경로라 에러가 아니다(§4.3-3 입력 무시).
            Debug.Log($"[영토] 확보할 수 없는 노드입니다: {nodeId} ({node.State})");
            return false;
        }

        node.State = TerritoryState.Owned;
        PromoteLockedNeighbors(node);

        OnNodeClaimed?.Invoke(node);
        OnChanged?.Invoke();
        return true;
    }

    // 새 보유 노드의 이웃 중 Locked를 Selectable로 승격 — 프론티어 갱신(§4.3-2).
    private void PromoteLockedNeighbors(TerritoryNode owned)
    {
        var neighborIds = owned.NeighborIds;
        for (int i = 0; i < neighborIds.Count; i++)
        {
            if (!_nodesById.TryGetValue(neighborIds[i], out var neighbor))
            {
                Debug.LogError($"[영토] {owned.Id}번 노드가 존재하지 않는 이웃을 참조합니다: {neighborIds[i]}");
                continue;
            }

            if (neighbor.State == TerritoryState.Locked)
            {
                neighbor.State = TerritoryState.Selectable;
            }
        }
    }
}
