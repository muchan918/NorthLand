using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 영토 확장의 씬 진입점(얇은 MonoBehaviour). 그래프 모델을 소유·구축하고,
/// 뷰/입력이 호출할 유일한 변경 진입점(<see cref="TryClaim"/>)을 제공한다 — ManagementController와 동일 계보.<br/>
/// 정책(하루 1회 게이팅)은 <see cref="TryClaim"/>에 구현됨(이슈 #67) — 비용 게이팅과 효과 적용(WL-030)은
/// 여전히 미착수.
/// </summary>
public class TerritoryController : MonoBehaviour
{
    public static TerritoryController Instance { get; private set; }

    [Tooltip("절차 생성 파라미터 (TerritoryGraph.md §4.1)")]
    [SerializeField] TerritoryGraphGenSettings _settings = new();

    [Tooltip("생성 시드. 0이면 매 플레이 랜덤 — 사용된 시드를 로그로 남겨 재현 가능하게 한다")]
    [SerializeField] int _seed = 0;

    /// <summary>그래프 상태가 바뀔 때 발생(Graph.OnChanged 중계). 뷰는 이걸 구독해 다시 렌더한다.</summary>
    public event Action OnChanged;

    /// <summary>읽기 질의용 모델. 상태 변경은 반드시 <see cref="TryClaim"/>으로만.</summary>
    public TerritoryGraph Graph { get; private set; }

    /// <summary>오늘 이미 영토를 확장했는가 — 낮 시작마다 초기화, 하루 1회만 true로 전이(이슈 #67).</summary>
    public bool HasExpandedToday { get; private set; }

    private DayNightManager _dayNight;

    // 모델은 Awake에서 구축한다 — 뷰의 Start()가 어떤 순서로 돌든 그래프가 준비돼 있도록(ManagementController와 동일 이유).
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 시드 0 = 랜덤 런. 이때도 실제 사용한 시드를 로그로 남긴다 — 버그 재현 시 인스펙터에 넣으면 같은 지형(WL-008).
        int seed = _seed != 0 ? _seed : new System.Random().Next(1, int.MaxValue);
        Debug.Log($"[영토] 그래프 생성 seed={seed} (재현: 컨트롤러 _seed에 입력)");

        Graph = BuildGeneratedGraph(_settings, seed);
        Graph.OnChanged += HandleGraphChanged;
    }

    private void Start()
    {
        SubscribeDayNight();
    }

    private void OnDestroy()
    {
        if (_dayNight != null)
        {
            _dayNight.OnDayStart -= HandleDayStart;
        }
    }

    private void SubscribeDayNight()
    {
        _dayNight = DayNightManager.Instance;
        if (_dayNight == null)
        {
            Debug.LogWarning("[영토] DayNightManager가 씬에 없습니다. 하루 1회 확장 제한이 초기화되지 않습니다.");
            return;
        }

        _dayNight.OnDayStart += HandleDayStart;
    }

    // OnChanged도 함께 발행해야 한다 — DayNightManager.EndNight()는 OnNightToDay(ManagementController가
    // 구독, 그 시점엔 아직 HasExpandedToday가 리셋 전) 다음에 OnDayStart를 발행하므로, 여기서 통지하지
    // 않으면 리셋된 상태가 UI(ProductionLineView)에 한 프레임도 반영되지 않고 이전 값으로 굳어버린다.
    private void HandleDayStart()
    {
        HasExpandedToday = false;
        OnChanged?.Invoke();
    }

    /// <summary>노드 확보 시도 — 뷰/입력의 유일한 변경 진입점. 판정은 모델(구조)·정책(하루 1회 게이팅)이 한다.</summary>
    public bool TryClaim(int nodeId)
    {
        if (Graph == null)
        {
            Debug.LogError("[영토] 그래프가 준비되지 않아 확보할 수 없습니다.");
            return false;
        }

        if (HasExpandedToday)
        {
            Debug.Log($"[영토] 오늘은 이미 영토를 확장했습니다. (노드 {nodeId})");
            return false;
        }

        // Graph.TryClaim은 성공 시 자기 OnChanged를 즉시 발행하고, 그게 HandleGraphChanged를 거쳐
        // 이 컨트롤러의 OnChanged로 동기 중계된다 — 즉 아래 HasExpandedToday = true보다 먼저
        // 구독자(UI)에게 통지가 도달한다. 그 통지 시점엔 HasExpandedToday가 아직 false라 UI가
        // 잠긴 상태로 갱신되고 이후 아무도 다시 통지하지 않아 그대로 굳는다 — 그래서 플래그를 세운
        // 뒤 한 번 더 명시적으로 OnChanged를 쏴 최종 상태로 다시 갱신시킨다.
        bool claimed = Graph.TryClaim(nodeId);
        if (claimed)
        {
            HasExpandedToday = true;
            OnChanged?.Invoke();
        }

        return claimed;
    }

    private void HandleGraphChanged() => OnChanged?.Invoke();

    // 생성 파이프라인(형태) → 모델(상태) 조립. 생성기는 위치·엣지만 알고 모델을 모르므로
    // 여기서 Vector2(2D) → XZ 평면 Vector3 변환과 노드 연결을 수행한다.
    private static TerritoryGraph BuildGeneratedGraph(TerritoryGraphGenSettings settings, int seed)
    {
        TerritoryGraphLayout layout = TerritoryGraphGenerator.Generate(settings, seed);

        var nodes = new TerritoryNode[layout.Positions.Count];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new TerritoryNode(i, new Vector3(layout.Positions[i].x, 0f, layout.Positions[i].y));
        }

        for (int i = 0; i < layout.Edges.Count; i++)
        {
            TerritoryNode.Connect(nodes[layout.Edges[i].A], nodes[layout.Edges[i].B]);
        }

        return new TerritoryGraph(nodes, layout.HomeNodeId);
    }
}
