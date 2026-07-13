using UnityEngine;

/// <summary>
/// 영토 노드 1개의 뷰(프리팹 루트) — 상태→시각 매핑만 담당한다.<br/>
/// 시각(스피어)은 자식 Visual로 분리: 실제 영토 아트로 교체할 때 자식만 갈아끼우면
/// 콜라이더·로직·프리팹 연결이 그대로 유지된다.<br/>
/// 콜라이더는 반드시 이 컴포넌트와 <b>같은 GameObject(프리팹 루트)</b>에 있어야 한다 —
/// MouseManager의 <c>hit.collider.TryGetComponent</c> 판정이 부모를 탐색하지 않기 때문(입력 통합 단계에서 사용).
/// </summary>
[RequireComponent(typeof(Collider))]
public class TerritoryNodeView : MonoBehaviour, ISelectable
{
    [Tooltip("상태색을 입힐 자식 Visual의 렌더러")]
    [SerializeField] Renderer _visual;

    [Tooltip("보유 영토 색 (GDD §6.3 노란색)")]
    [SerializeField] Color _ownedColor = new(1f, 0.85f, 0.2f);

    [Tooltip("프론티어(선택 가능) 색 (GDD §6.3 회색)")]
    [SerializeField] Color _selectableColor = new(0.65f, 0.65f, 0.65f);

    private TerritoryController _controller;
    private int _nodeId = -1;

    public int NodeId => _nodeId;

    /// <summary>그래프 뷰가 스폰 직후 호출한다. 모델과의 연결점은 (컨트롤러, 노드 Id) 둘뿐.</summary>
    public void Bind(TerritoryController controller, int nodeId)
    {
        _controller = controller;
        _nodeId = nodeId;
        Refresh();
    }

    public void OnDeselected() => Refresh();

    public void OnSelected()
    {
        if (_controller == null || _controller.Graph == null)
        {
            Debug.LogError("[영토] 컨트롤러/그래프가 준비되지 않아 선택할 수 없습니다.", this);
            return;
        }

        if (_controller.TryClaim(_nodeId))
        {
            Debug.Log($"[영토] 노드 {_nodeId} 선택으로 확보 성공", this);
        }
    }


    /// <summary>모델 상태를 시각에 반영한다. Locked는 완전 숨김(GDD §4.2 점진 공개 — 스펙 확정).</summary>
    public void Refresh()
    {
        if (_controller == null || _controller.Graph == null)
        {
            return;
        }

        var node = _controller.Graph.GetNode(_nodeId);
        if (node == null)
        {
            Debug.LogError($"[영토] 노드 뷰가 존재하지 않는 노드에 바인딩됐습니다: {_nodeId}");
            return;
        }

        bool revealed = node.State != TerritoryState.Locked;
        gameObject.SetActive(revealed);
        if (!revealed || _visual == null)
        {
            return;
        }

        _visual.material.color =
            node.State == TerritoryState.Owned ? _ownedColor : _selectableColor;
    }
}
