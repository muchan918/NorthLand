using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 경영 자원 시스템의 애플리케이션 로직(UI 무관). 지갑·생산처·주민 배치 상태를 소유하고,
/// DayNightManager 전환 이벤트에 반응해 정산/초기화한다. UI(<see cref="ManagementPanelView"/>)는
/// 이 컨트롤러만 구독·호출하므로, 실제 UI 아트로 교체해도 이 클래스는 바뀌지 않는다.<br/>
/// <br/>
/// - 낮→밤(OnDayToNight): 주민 배치 확정 (자원 정산 없음, 팀 계약 #5)<br/>
/// - 밤→낮(OnNightToDay): 각 생산처 Produce로 자원 정산(먼저) + 주민 배치 초기화(그 다음) (팀 계약 #5)<br/>
/// - 주민 수·풀(maxVillagers)은 주민 시스템 부재로 임시 placeholder — 주민 시스템이 생기면 이 부분만 교체.<br/>
/// (Docs/ManagementArea/Resources.md — 이슈 #43)
/// </summary>
public class ManagementController : MonoBehaviour
{
    [Tooltip("생산 라인으로 만들 산출 자원들. ResourceID가 ResourceTable CSV(wood/iron/food/mana)와 일치해야 한다.")]
    [SerializeField] ResourceAsset[] _resourceAssets;

    [Tooltip("주민 1명당 생산량 (모든 라인 공통, 임시값)")]
    [SerializeField] int _baseAmountPerVillager = 5;

    [Tooltip("총 보유(최대) 주민 수. 주민 시스템 부재로 임시 placeholder. 전 라인 배치 합계 상한.")]
    [SerializeField] int _maxVillagers = 5;

    /// <summary>상태(자원·주민 배치·페이즈)가 바뀔 때 발생. 뷰는 이걸 구독해 다시 렌더한다.</summary>
    public event Action OnChanged;

    private ResourceWallet _wallet;
    private ProductionModifiers _productionModifiers;
    private ResourceProductionSource[] _sources;
    private ResourceAsset[] _lineAssets;
    private int[] _villagerCounts;

    private DayNightManager _dayNight;
    private TerritoryController _territory;

    public int LineCount => _sources != null ? _sources.Length : 0;
    public int MaxVillagers => _maxVillagers;
    public int AssignedTotal
    {
        get
        {
            int total = 0;
            if (_villagerCounts != null)
            {
                for (int i = 0; i < _villagerCounts.Length; i++)
                {
                    total += _villagerCounts[i];
                }
            }
            return total;
        }
    }

    // DayNightManager가 씬에 없으면(null) 낮으로 간주해 패널이 단독으로도 동작하게 한다.
    public bool IsDay => _dayNight == null || _dayNight.CurrentPhase == DayNightManager.Phase.Day;
    public int WaveCount => _dayNight != null ? _dayNight.WaveCount : 0;

    // 영토가 씬에 없으면(null) 게이트 없이 배치 허용(permissive) — IsDay와 동일한 패턴.
    public bool CanAssignVillagers => _territory == null || _territory.HasExpandedToday;

    // 잉여 주민이 없어야(전원 배치) 밤으로 전환 가능.
    public bool CanEndDay => IsDay && AssignedTotal >= _maxVillagers;

    // 페이즈 전환 버튼 활성 조건: 낮이면 전원 배치돼야, 밤이면 언제든 가능(웨이브 종료 대역).
    public bool CanAdvancePhase => _dayNight != null && (!IsDay || CanEndDay);

    public int ResourceCount(ResourceKind kind) => _wallet != null ? _wallet.Get(kind) : 0;

    // ── 비용 소비 게이트웨이 (소비처는 지갑에 직접 접근하지 않고 컨트롤러 경유 — WL-017) ──
    /// <summary>Cost 리스트를 감당할 수 있는지 판정한다. null/빈 리스트는 무료(true).</summary>
    public bool CanAfford(IReadOnlyList<ResourceCost> costs)
    {
        if (costs == null || costs.Count == 0) return true; // 무료 — 매 프레임 조회 시 할당 회피
        if (_wallet == null) return false;
        foreach (KeyValuePair<ResourceKind, int> need in AggregateCost(costs))
        {
            if (!_wallet.CanAfford(need.Key, need.Value)) return false;
        }
        return true;
    }

    /// <summary>Cost 리스트를 차감한다. 전부 감당 가능할 때만 전부 차감하고 true,
    /// 하나라도 부족하면 아무것도 쓰지 않고 false를 반환한다(원자적).</summary>
    public bool TrySpend(IReadOnlyList<ResourceCost> costs)
    {
        if (_wallet == null) return false;

        Dictionary<ResourceKind, int> needs = AggregateCost(costs);
        foreach (KeyValuePair<ResourceKind, int> need in needs)
        {
            if (!_wallet.CanAfford(need.Key, need.Value)) return false;
        }
        foreach (KeyValuePair<ResourceKind, int> need in needs)
        {
            _wallet.TrySpend(need.Key, need.Value);
        }
        return true;
    }

    // Cost 리스트를 (ResourceKind → 합산 수량)으로 해석한다. Resource 미지정·수량 0 항목은 스킵.
    // ResourceAsset.Data는 호출부 채움 규약(SystemMap §2) — null이면 ResourceTable에서 채운다.
    private Dictionary<ResourceKind, int> AggregateCost(IReadOnlyList<ResourceCost> costs)
    {
        var totals = new Dictionary<ResourceKind, int>();
        if (costs == null) return totals;

        ResourceTable table = null;
        for (int i = 0; i < costs.Count; i++)
        {
            ResourceCost cost = costs[i];
            if (cost == null || cost.Resource == null || cost.Amount <= 0) continue;

            if (cost.Resource.Data == null)
            {
                table ??= DataTableManager.Get<ResourceTable>("ResourceTable");
                if (table != null) cost.Resource.Data = table.Get(cost.Resource.ResourceID);
            }
            if (cost.Resource.Data == null)
            {
                Debug.LogError($"[경영] 비용 자원 '{cost.Resource.ResourceID}' Data를 채우지 못했습니다.");
                continue;
            }

            ResourceKind kind = cost.Resource.Data.Kind;
            totals.TryGetValue(kind, out int cur);
            totals[kind] = cur + cost.Amount;
        }
        return totals;
    }

    public string LineDisplayName(int index) => IsValidLine(index) ? LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, _lineAssets[index].Data.NameKey) : "-";
    public ResourceKind LineKind(int index) => IsValidLine(index) ? _lineAssets[index].Data.Kind : default;
    public int LineVillagers(int index) => IsValidLine(index) ? _villagerCounts[index] : 0;
    // 패시브 생산 배율(영토 효과 등)을 반영한 예상 생산량. 정산부(HandleNightToDay)와 같은 식이어야 UI가 실제와 일치한다.
    public int LineExpectedProduction(int index) =>
        IsValidLine(index) ? Mathf.RoundToInt(_baseAmountPerVillager * LineVillagers(index) * ProductionMultiplier(index)) : 0;

    // 라인 산출 자원의 현재 생산 배율(레지스트리 미준비 시 1.0).
    private float ProductionMultiplier(int index) =>
        _productionModifiers != null && IsValidLine(index) ? _productionModifiers.GetMultiplier(_lineAssets[index].Data.Kind) : 1f;

    // 모델은 Awake에서 구축한다 — 뷰의 Start()가 어떤 순서로 돌든 라인 수가 준비돼 있도록.
    private void Awake()
    {
        BuildModel();
    }

    private void Start()
    {
        SubscribeDayNight();
        SubscribeTerritory();
        OnChanged?.Invoke();
    }

    private void OnDestroy()
    {
        if (_dayNight != null)
        {
            _dayNight.OnNightToDay -= HandleNightToDay;
        }
        if (_territory != null)
        {
            _territory.OnChanged -= HandleTerritoryChanged;
            if (_territory.Graph != null)
            {
                _territory.Graph.OnNodeClaimed -= HandleNodeClaimed;
            }
        }
    }

    private void BuildModel()
    {
        _wallet = new ResourceWallet();
        // 지갑 잔액이 바뀌면(획득·차감) 컨트롤러 OnChanged로 재발화 → 패널/HUD가 갱신된다.
        // (지갑 직접 변경엔 원래 OnChanged가 안 돌던 지점을 여기서 메운다)
        _wallet.OnChanged += (_, _) => OnChanged?.Invoke();

        // 생산 배율 레지스트리는 지갑과 함께 런마다 새로 만든다(영토 패시브 효과가 여기에 누적).
        _productionModifiers = new ProductionModifiers();

        ResourceTable table = DataTableManager.Get<ResourceTable>("ResourceTable");

        var assets = new List<ResourceAsset>();
        var sources = new List<ResourceProductionSource>();

        int count = _resourceAssets != null ? _resourceAssets.Length : 0;
        for (int i = 0; i < count; i++)
        {
            ResourceAsset asset = _resourceAssets[i];
            if (asset == null)
            {
                Debug.LogError($"[경영] {i}번 ResourceAsset이 비어 있습니다.");
                continue;
            }

            // ResourceAsset.Data는 호출부가 채우는 규약(SystemMap §2).
            if (asset.Data == null && table != null)
            {
                asset.Data = table.Get(asset.ResourceID);
            }
            if (asset.Data == null)
            {
                Debug.LogError($"[경영] '{asset.ResourceID}' Data를 채우지 못해 라인 제외.");
                continue;
            }

            assets.Add(asset);
            sources.Add(new ResourceProductionSource(asset, _baseAmountPerVillager, _wallet));
        }

        _lineAssets = assets.ToArray();
        _sources = sources.ToArray();
        _villagerCounts = new int[_sources.Length];
    }

    private void SubscribeDayNight()
    {
        _dayNight = DayNightManager.Instance;
        if (_dayNight == null)
        {
            Debug.LogWarning("[경영] DayNightManager가 씬에 없습니다. 정산·초기화가 자동 연동되지 않습니다.");
            return;
        }

        _dayNight.OnNightToDay += HandleNightToDay;
    }

    private void SubscribeTerritory()
    {
        _territory = TerritoryController.Instance;
        if (_territory == null)
        {
            Debug.LogWarning("[경영] TerritoryController가 씬에 없습니다. 영토 확장 없이도 주민을 배치할 수 있습니다.");
            return;
        }

        _territory.OnChanged += HandleTerritoryChanged;

        // 영토 확보 효과 적용 지점(WL-030): 확보 시 1회 발행되는 OnNodeClaimed를 구독해 그 노드의 효과를
        // 적용한다. 지갑·생산 배율을 이 컨트롤러가 소유하므로 여기서 컨텍스트를 조립하는 게 자연스럽다.
        if (_territory.Graph != null)
        {
            _territory.Graph.OnNodeClaimed += HandleNodeClaimed;
        }
    }

    private void HandleTerritoryChanged() => OnChanged?.Invoke();

    // 확보된 노드에 주입된 정의의 효과들을 적용한다(즉시 자원 지급·패시브 생산 배율 등). 정의가 없는 노드(본진)는 무시.
    // 지갑 지급은 wallet.OnChanged → OnChanged로, 배율 변경은 이어지는 Graph.OnChanged 중계로 UI에 반영된다.
    private void HandleNodeClaimed(TerritoryNode node)
    {
        if (node == null || node.Definition == null)
        {
            return;
        }

        var ctx = new TerritoryEffectContext(_wallet, _productionModifiers, node, _territory != null ? _territory.Graph : null);
        node.Definition.ApplyAll(ctx);
    }

    // ── 뷰(또는 후속 패널 버튼)가 호출하는 진입점 ─────────────────────────
    public void AssignVillager(int index)
    {
        if (!CanEditLine(index))
        {
            return;
        }
        if (AssignedTotal >= _maxVillagers)
        {
            Debug.Log($"[경영] 가용 주민이 없습니다. (배치 {AssignedTotal}/{_maxVillagers})");
            return;
        }

        _villagerCounts[index]++;
        OnChanged?.Invoke();
    }

    public void UnassignVillager(int index)
    {
        if (!CanEditLine(index))
        {
            return;
        }
        if (_villagerCounts[index] <= 0)
        {
            return;
        }

        _villagerCounts[index]--;
        OnChanged?.Invoke();
    }

    // 페이즈 전환 버튼: 낮이면 밤으로(잉여 주민 게이트). 밤→낮(EndNight)은 이제 웨이브 성공
    // 버튼이 전담한다(WL-018) — 이 버튼은 밤에는 아무 동작도 하지 않는다.
    public void RequestAdvancePhase()
    {
        if (_dayNight == null)
        {
            Debug.LogWarning("[경영] DayNightManager가 없어 페이즈를 전환할 수 없습니다.");
            return;
        }

        if (!IsDay)
        {
            return;
        }

        if (!CanEndDay)
        {
            Debug.Log($"[경영] 잉여 주민이 있어 밤으로 전환할 수 없습니다. (배치 {AssignedTotal}/{_maxVillagers})");
            return;
        }
        _dayNight.EndDay();
    }

    // ── DayNightManager 이벤트 훅 (팀 계약 #5) ──────────────────────────
    private void HandleNightToDay()
    {
        for (int i = 0; i < _sources.Length; i++)
        {
            if (_sources[i] == null)
            {
                continue;
            }

            int produced = _sources[i].Produce(_villagerCounts[i], ProductionMultiplier(i));
            Debug.Log($"[정산] {LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, _lineAssets[i].Data.NameKey)}: 주민 {_villagerCounts[i]}명 → +{produced}");
        }

        for (int i = 0; i < _villagerCounts.Length; i++)
        {
            _villagerCounts[i] = 0;
        }

        Debug.Log($"[경영] 밤 → 낮 (Wave {WaveCount}): 자원 정산 + 주민 배치 초기화");
        OnChanged?.Invoke();
    }

    private bool CanEditLine(int index)
    {
        if (!IsDay)
        {
            Debug.Log("[경영] 밤에는 배치를 변경할 수 없습니다.");
            return false;
        }
        if (!CanAssignVillagers)
        {
            Debug.Log("[경영] 오늘 아직 영토를 확장하지 않아 주민을 배치할 수 없습니다.");
            return false;
        }
        return IsValidLine(index);
    }

    private bool IsValidLine(int index) =>
        _sources != null && index >= 0 && index < _sources.Length && _sources[index] != null;
}
