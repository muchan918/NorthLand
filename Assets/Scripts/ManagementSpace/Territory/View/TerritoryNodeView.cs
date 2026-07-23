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

    [Header("호버 툴팁 색 (이름/설명)")]
    [Tooltip("툴팁 헤더(이름) 영역 배경색")]
    [SerializeField] Color _tooltipHeaderColor = new(0.20f, 0.35f, 0.50f, 0.95f);

    [Tooltip("툴팁 본문(설명) 영역 배경색")]
    [SerializeField] Color _tooltipBackgroundColor = new(0.10f, 0.12f, 0.16f, 0.95f);

    private TerritoryController _controller;
    private int _nodeId = -1;
    private bool _isHovered;

    // #93: 상태별 비주얼 스왑(소용돌이/땅) 컴포넌트. 구형 프리팹엔 없으므로 색상 경로로 폴백한다.
    private TerritoryNodeStateVisual _stateVisual;
    private bool _pendingClaimFx;

    public int NodeId => _nodeId;

    // 툴팁 일일 수급 표기용(#166): 포맷 스트링 키 + 자원 표시명 키 규약(ManagementPanelView와 동일 game.resources.{종류}).
    private const string k_DailySupplyKey = "game.territory.daily_supply";
    private static string ResourceNameKey(ResourceKind kind) => $"game.resources.{kind.ToString().ToLowerInvariant()}";

    private void Awake()
    {
        _stateVisual = GetComponent<TerritoryNodeStateVisual>();
    }

    /// <summary>그래프 뷰가 스폰 직후 호출한다. 모델과의 연결점은 (컨트롤러, 노드 Id) 둘뿐.</summary>
    public void Bind(TerritoryController controller, int nodeId)
    {
        _controller = controller;
        _nodeId = nodeId;

        // 확보 연출 트리거(#93): OnNodeClaimed는 확보 시 정확히 1회, OnChanged보다 먼저 발행되므로
        // 직후 도달하는 Refresh가 플래그를 소비해 연출 경로로 들어간다. 본진·재시작처럼 이벤트 없이
        // Owned인 노드는 플래그가 없어 자연히 연출 없이 완성 상태가 된다.
        if (_stateVisual != null && _controller != null && _controller.Graph != null)
        {
            _controller.Graph.OnNodeClaimed += HandleClaimed;
        }

        Refresh();
    }

    private void OnDestroy()
    {
        if (_controller != null && _controller.Graph != null)
        {
            _controller.Graph.OnNodeClaimed -= HandleClaimed;
        }
    }

    private void HandleClaimed(TerritoryNode node)
    {
        if (node != null && node.Id == _nodeId)
        {
            _pendingClaimFx = true;
        }
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

        // 본진은 확보 대상이 아니다(초기 Owned) — TryClaim이 어차피 실패하지만, 실패 로그·선택 churn을
        // 남기지 않도록 진입 자체를 막는다. Selectable 판정은 모델(TryClaim)이 최종적으로 담당.
        if (_nodeId == _controller.Graph.HomeNodeId)
        {
            return;
        }

        if (_controller.TryClaim(_nodeId))
        {
            Debug.Log($"[영토] 노드 {_nodeId} 선택으로 확보 성공", this);
        }
    }

    // 호버 시 이 노드에 주입된 영토 정의의 이름·설명을 툴팁으로 공급한다(pull — BuildingTooltipSource 계보).
    // 정의가 없는 노드(본진·미할당)는 null을 반환해 툴팁을 띄우지 않는다. 색 하이라이트는 OnHoverEnter/Exit가 별도로 담당.
    public TooltipContent? GetTooltipContent()
    {
        if (_controller == null || _controller.Graph == null)
        {
            return null;
        }

        var node = _controller.Graph.GetNode(_nodeId);
        var def = node?.Definition;
        if (def == null)
        {
            return null;
        }

        // 동기 pull 조회(로케일 변경 자동 갱신 불필요 — 호버 시점 1회). 헤더 = 영지 이름.
        string name = LocalizationHelper.Get(LocalizationHelper.k_TerritoriesTable, def.DisplayNameKey);

        // 본문 = 일일 수급 문장(#166): "매일 {0} {1}개를 획득합니다." 스트링 테이블 포맷을 string.Format으로 채운다.
        // {1}=주입 시 확정된 노드별 DailyYield — Selectable(확보 전) 노드도 값이 있어 "확보하면 매일 몇 개" 미리보기가 된다.
        string resourceName = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, ResourceNameKey(def.Kind));
        string fmt = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, k_DailySupplyKey);
        // 포맷 키가 없거나(안내 문자열) 플레이스홀더가 없으면 언어 중립 폴백으로.
        string body = (!string.IsNullOrEmpty(fmt) && fmt.Contains("{0}"))
            ? string.Format(fmt, resourceName, node.DailyYield)
            : $"{resourceName} +{node.DailyYield}";

        return new(name, body, _tooltipHeaderColor, _tooltipBackgroundColor);
    }

    public void OnHoverEnter()
    {
        if (IsHome) return; // 본진은 호버 연출 없음(GDD §6.3.1)

        _isHovered = true;

        Refresh();
    }

    public void OnHoverExit()
    {
        if (IsHome) return; // 본진은 호버 연출 없음(GDD §6.3.1)

        _isHovered = false;

        Refresh();
    }

    // 본진 판정은 하드코딩 0이 아니라 모델의 HomeNodeId 기준 — 본진 인덱스가 바뀌어도 어긋나지 않는다.
    // 컨트롤러/그래프 미준비 시엔 본진으로 취급하지 않는다(가드는 각 호출부가 담당).
    private bool IsHome => _controller != null && _controller.Graph != null && _nodeId == _controller.Graph.HomeNodeId;

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
        if (!revealed)
        {
            return;
        }

        bool canClaimNow = node.State == TerritoryState.Selectable && !_controller.HasExpandedToday;

        // #93: 스왑 컴포넌트가 있으면(신형 프리팹) 소용돌이/땅 비주얼에 위임하고,
        // 없으면(구형 프리팹, GameScene 등 기존 씬) 아래 색상 경로를 그대로 쓴다.
        if (_stateVisual != null)
        {
            bool isHome = IsHome;
            bool playClaimFx = _pendingClaimFx;
            _pendingClaimFx = false;
            // 섬 프리팹은 이 노드 영지 SO가 소유한다(#166). 영지 미할당(본진 등)이면 null → 상태 비주얼이 폴백 처리.
            GameObject islandPrefab = node.Definition != null ? node.Definition.IslandPrefab : null;
            _stateVisual.Apply(node.State, isHome, _nodeId, _isHovered, canClaimNow, playClaimFx, islandPrefab);
            return;
        }

        if (_visual == null)
        {
            return;
        }

        // Refresh()는 Bind/OnDeselected/컨트롤러 OnChanged(다른 노드 클레임·낮 시작으로 인한 그래프
        // 전체 갱신) 세 경로에서 재진입되므로, 호버 중 갱신이 호버 틴트를 덮어쓰지 않도록 색 결정
        // 마지막에 _isHovered를 우선한다. 단, 하이라이트는 "지금 선택 가능함"을 알리는 용도이므로
        // Selectable 상태이면서 오늘 아직 확장하지 않은 경우(HasExpandedToday == false)에만 적용한다
        // — 이미 확보한(Owned) 영토는 물론, 오늘 확장을 다 쓴 뒤의 회색 노드도 호버해도 색이 그대로다.
        Color stateColor = node.State == TerritoryState.Owned ? _ownedColor : _selectableColor;
        bool applyHover = _isHovered && canClaimNow;
        _visual.material.color = applyHover ? _hoverColor : stateColor;
    }
}
