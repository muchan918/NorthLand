using UnityEngine;

/// <summary>
/// 영토 노드 1개의 뷰(프리팹 루트) — 상태→시각 매핑만 담당한다.<br/>
/// 시각(스피어)은 자식 Visual로 분리: 실제 영토 아트로 교체할 때 자식만 갈아끼우면
/// 콜라이더·로직·프리팹 연결이 그대로 유지된다.<br/>
/// 콜라이더는 반드시 이 컴포넌트와 <b>같은 GameObject(프리팹 루트)</b>에 있어야 한다 —
/// MouseManager의 <c>hit.collider.TryGetComponent</c> 판정이 부모를 탐색하지 않기 때문(입력 통합 단계에서 사용).
/// </summary>
// 레이어 확인 완료(WL-005 해소, #67): 프리팹은 Layer 6(Selectable)이고 MouseManager._selectableMask도
// 이 비트를 포함해 클릭/호버 모두 정상 동작함을 실제 씬에서 확인함.
[RequireComponent(typeof(Collider))]
public class TerritoryNodeView : MonoBehaviour, ISelectable, IHoverable
{
    [Tooltip("상태색을 입힐 자식 Visual의 렌더러")]
    [SerializeField] Renderer _visual;

    [Tooltip("보유 영토 색 (GDD §6.3 노란색)")]
    [SerializeField] Color _ownedColor = new(1f, 0.85f, 0.2f);

    [Tooltip("프론티어(선택 가능) 색 (GDD §6.3 회색)")]
    [SerializeField] Color _selectableColor = new(0.65f, 0.65f, 0.65f);

    [Tooltip("호버 시 색 (#67 호버 하이라이트)")]
    [SerializeField] Color _hoverColor = new(0.9f, 0.85f, 0.4f);

    private TerritoryController _controller;
    private int _nodeId = -1;
    private bool _isHovered;

    public int NodeId => _nodeId;

    /// <summary>그래프 뷰가 스폰 직후 호출한다. 모델과의 연결점은 (컨트롤러, 노드 Id) 둘뿐.</summary>
    public void Bind(TerritoryController controller, int nodeId)
    {
        _controller = controller;
        _nodeId = nodeId;
        Refresh();
    }

    public void OnDeselected() => Refresh();

    // TODO: 지금은 클릭 1회 = 즉시 확보(비가역)라 ISelectable의 "가역적 조회" 시맨틱을 오버로드한다.
    //       비용(GDD §4.2 마나석)·낮/밤 게이팅(§5.1) 도입 시 호버=미리보기 / 클릭=확정(또는 확정 버튼)
    //       분리를 검토할 것 (WL-011 선택 통지 이중 경로와 인접).
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

    // 영토 노드는 툴팁 없음 — 호버는 색 변경 전용(BuildingTooltipSource 경로와 독립, #67).
    public TooltipContent? GetTooltipContent() => null;

    public void OnHoverEnter()
    {
        _isHovered = true;
        Refresh();
    }

    public void OnHoverExit()
    {
        _isHovered = false;
        Refresh();
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

        // Refresh()는 Bind/OnDeselected/컨트롤러 OnChanged(다른 노드 클레임·낮 시작으로 인한 그래프
        // 전체 갱신) 세 경로에서 재진입되므로, 호버 중 갱신이 호버 틴트를 덮어쓰지 않도록 색 결정
        // 마지막에 _isHovered를 우선한다. 단, 하이라이트는 "지금 선택 가능함"을 알리는 용도이므로
        // Selectable 상태이면서 오늘 아직 확장하지 않은 경우(HasExpandedToday == false)에만 적용한다
        // — 이미 확보한(Owned) 영토는 물론, 오늘 확장을 다 쓴 뒤의 회색 노드도 호버해도 색이 그대로다.
        Color stateColor = node.State == TerritoryState.Owned ? _ownedColor : _selectableColor;
        bool canClaimNow = node.State == TerritoryState.Selectable && !_controller.HasExpandedToday;
        bool applyHover = _isHovered && canClaimNow;
        _visual.material.color = applyHover ? _hoverColor : stateColor;
    }
}
