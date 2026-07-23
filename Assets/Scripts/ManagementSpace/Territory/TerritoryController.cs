using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 영토 확장의 씬 진입점(얇은 MonoBehaviour). 그래프 모델을 소유·구축하고,
/// 뷰/입력이 호출할 유일한 변경 진입점(<see cref="TryClaim"/>)을 제공한다 — ManagementController와 동일 계보.<br/>
/// 정책(하루 1회 게이팅)은 <see cref="TryClaim"/>에 구현됨(이슈 #67). 확보 후 자원 수급은 즉시지급이 아니라
/// <b>매일 정산 시 자동 수급</b>(#166) — 노드의 <see cref="TerritoryDefinition"/>/DailyYield를 ManagementController가 소비한다.
/// </summary>
public class TerritoryController : MonoBehaviour
{
    public static TerritoryController Instance { get; private set; }

    [Tooltip("절차 생성 파라미터 (TerritoryGraph.md §4.1)")]
    [SerializeField] TerritoryGraphGenSettings _settings = new();

    [Tooltip("생성 시드. 0이면 매 플레이 랜덤 — 사용된 시드를 로그로 남겨 재현 가능하게 한다")]
    [SerializeField] int _seed = 0;

    [Tooltip("노드에 랜덤 배정할 미개척 영지 정의 풀(금/루비/사파이어/다이아 등). 비우면 영지 없이 지형만 생성된다(TerritoryGraph.md §5).")]
    [SerializeField] List<TerritoryDefinition> _definitionPool = new();

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

        Graph = BuildGeneratedGraph(_settings, seed, _definitionPool);
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
    // 여기서 Vector2(2D) → XZ 평면 Vector3 변환과 노드 연결을 수행하고, 마지막에 효과 정의를 주입한다.
    private static TerritoryGraph BuildGeneratedGraph(TerritoryGraphGenSettings settings, int seed, IReadOnlyList<TerritoryDefinition> definitionPool)
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

        AssignDefinitions(nodes, layout.HomeNodeId, definitionPool, seed);

        return new TerritoryGraph(nodes, layout.HomeNodeId);
    }

    // 노드에 미개척 영지 정의를 배정하고 각 노드의 일일 수급량을 롤한다 — 본진은 영지 없음(제외, #166).
    // 시드로 셔플·롤하므로 재현 가능(WL-008 계보): 같은 seed면 같은 지형 + 같은 영지 배치 + 같은 일일량.
    //
    // 풀(자원 종류)보다 노드가 많은 게 정상이다(특수 자원 4종 < 노드 최대 30) — 풀을 소진하면 다시
    // 셔플해 반복 배정하므로 같은 자원(예: 금)이 여러 노드에 정상적으로 재등장한다(GDD §3.2 "영지 수↑→총 수급↑").
    private static void AssignDefinitions(TerritoryNode[] nodes, int homeNodeId, IReadOnlyList<TerritoryDefinition> pool, int seed)
    {
        if (pool == null || pool.Count == 0)
        {
            return; // 영지 없이 지형만 — 유효한 초기/미설정 상태(§5 "풀을 비우면 지형만")
        }

        var rng = new System.Random(seed);
        var bag = new List<TerritoryDefinition>(pool.Count);

        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].Id == homeNodeId)
            {
                continue; // 본진은 영지 없음
            }

            if (bag.Count == 0)
            {
                RefillShuffled(bag, pool, rng);
                if (bag.Count == 0)
                {
                    Debug.LogWarning("[영토] 정의 풀에 유효한 항목이 없어 영지를 배정하지 않습니다.");
                    return;
                }
            }

            int last = bag.Count - 1;
            TerritoryDefinition def = bag[last];
            nodes[i].Definition = def;
            // 주입 시점에 일일 수급량을 [Min,Max]에서 1회 롤해 노드에 확정한다(#166). 같은 rng라 시드 결정성 유지.
            nodes[i].DailyYield = def.RollDailyYield(rng);
            bag.RemoveAt(last); // 끝에서 꺼내 O(1) — 순서가 이미 무작위라 어느 쪽에서 꺼내도 무방
        }
    }

    // 풀 전체(널 슬롯 제외)를 bag에 복사한 뒤 Fisher-Yates로 섞는다. 배정은 bag 끝에서 꺼내므로 순서만 무작위면 충분.
    private static void RefillShuffled(List<TerritoryDefinition> bag, IReadOnlyList<TerritoryDefinition> pool, System.Random rng)
    {
        bag.Clear();
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null)
            {
                bag.Add(pool[i]);
            }
        }

        for (int i = bag.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }
    }
}
