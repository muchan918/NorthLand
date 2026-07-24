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
    [Tooltip("생산 라인이 되는 생산 건물들(나무꾼의 집·광산·농장). 각 건물의 Production에서 산출 자원·주민당량·업그레이드 테이블을 읽는다.")]
    [SerializeField] BuildingAsset[] _productionBuildings;

    [Tooltip("총 보유(최대) 주민 수. 주민 시스템 부재로 임시 placeholder. 전 라인 배치 합계 상한.")]
    [SerializeField] int _maxVillagers = 5;

    [Tooltip("웨이브 클리어(밤→낮 정산) 시 지급되는 마나석 고정량 (GDD §4.3)")]
    [SerializeField] int _manaPerWaveClear = 10;

    [Tooltip("게임 시작(런당 1회) 시 지급되는 초기 나무/철/식량. 마나석은 영토 확장·전투 보상 전용이라 제외(팀 계약 #3, 이슈 #130)")]
    [SerializeField] int _initialWood = 110;
    [SerializeField] int _initialIron = 40;
    [SerializeField] int _initialFood = 0;

    /// <summary>상태(자원·주민 배치·페이즈)가 바뀔 때 발생. 뷰는 이걸 구독해 다시 렌더한다.</summary>
    public event Action OnChanged;

    private ResourceWallet _wallet;
    private ProductionModifiers _productionModifiers;
    private ResourceProductionSource[] _sources;
    private ResourceAsset[] _lineAssets;
    private int[] _villagerCounts;

    // 건물 업그레이드 상태 — 라인별 런타임 상태로 소유한다(공유 SO에 레벨을 쓰지 않는다, WL-016).
    // _level[i]=현재 레벨(0=미업그레이드), _amountPerVillager[i]=그 레벨의 주민당 생산량(정산·예상치가 참조),
    // _lineUpgradeLevels[i]=그 건물의 레벨 테이블(SO에서 추출한 읽기 전용 기준값).
    private int[] _level;
    private int[] _amountPerVillager;
    private List<BuildingAsset.UpgradeLevel>[] _lineUpgradeLevels;
    private BuildingAsset[] _lineBuildings; // 라인 index → 원본 건물 SO (건물→라인 매핑용, BuildingInfoUI 등)

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

    // 지금 준비/진행 중인 웨이브 번호(1부터) — 패널 표시용. DayNightManager가 없으면 1일차로 간주.
    public int CurrentWave => _dayNight != null ? _dayNight.CurrentWave : 1;

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
        IsValidLine(index) ? Mathf.RoundToInt(_amountPerVillager[index] * LineVillagers(index) * ProductionMultiplier(index)) : 0;

    // ── 건물 업그레이드 조회 API (다음 이슈의 UI가 바인딩할 계약) ──────────
    public int LineLevel(int index) => IsValidLine(index) ? _level[index] : 0;
    public int LineMaxLevel(int index) => IsValidLine(index) ? _lineUpgradeLevels[index].Count : 0;
    public int LineAmountPerVillager(int index) => IsValidLine(index) ? _amountPerVillager[index] : 0;

    // 다음 레벨(=현재 레벨 인덱스)의 비용. 최대 레벨이거나 라인 무효면 null(표시부는 "MAX" 처리).
    public IReadOnlyList<ResourceCost> LineUpgradeCost(int index)
    {
        if (!IsValidLine(index)) return null;
        List<BuildingAsset.UpgradeLevel> levels = _lineUpgradeLevels[index];
        int next = _level[index];
        return next < levels.Count ? levels[next].Cost : null;
    }

    // 업그레이드 가능 여부: 낮이어야 하고, 다음 레벨이 있어야 하고, 그 비용을 감당할 수 있어야 한다.
    public bool CanUpgrade(int index)
    {
        if (!IsDay || !IsValidLine(index)) return false;
        List<BuildingAsset.UpgradeLevel> levels = _lineUpgradeLevels[index];
        int next = _level[index];
        return next < levels.Count && CanAfford(levels[next].Cost);
    }

    // 건물 SO가 몇 번 라인인지. 생산 라인(업그레이드 대상)이 아니면 -1.
    public int LineIndexOf(BuildingAsset building)
    {
        if (building == null || _lineBuildings == null) return -1;
        for (int i = 0; i < _lineBuildings.Length; i++)
        {
            if (_lineBuildings[i] == building) return i;
        }
        return -1;
    }

    // 한 단계 업그레이드 후의 주민당량(표시용 "현재 → 다음"). 최대 레벨이면 현재값 그대로(증가 없음).
    public int LineNextAmountPerVillager(int index)
    {
        if (!IsValidLine(index)) return 0;
        List<BuildingAsset.UpgradeLevel> levels = _lineUpgradeLevels[index];
        int next = _level[index];
        return next < levels.Count ? levels[next].AmountPerVillager : _amountPerVillager[index];
    }

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
        }
    }

    private void BuildModel()
    {
        _wallet = new ResourceWallet();
        // 지갑 잔액이 바뀌면(획득·차감) 컨트롤러 OnChanged로 재발화 → 패널/HUD가 갱신된다.
        // (지갑 직접 변경엔 원래 OnChanged가 안 돌던 지점을 여기서 메운다)
        _wallet.OnChanged += (_, _) => OnChanged?.Invoke();

        // 게임 시작 초기 자원 지급(런당 1회, 이슈 #130) — 유일 창구인 ResourceWallet.Add로만 지급한다(팀 계약 #3).
        _wallet.Add(ResourceKind.Wood, _initialWood);
        _wallet.Add(ResourceKind.Iron, _initialIron);
        _wallet.Add(ResourceKind.Food, _initialFood);
        Debug.Log($"[경영] 초기 자원 지급: Wood +{_initialWood}, Iron +{_initialIron}, Food +{_initialFood}");

        // 생산 배율 레지스트리는 지갑과 함께 런마다 새로 만든다(영토 패시브 효과가 여기에 누적).
        _productionModifiers = new ProductionModifiers();

        ResourceTable table = DataTableManager.Get<ResourceTable>("ResourceTable");

        var assets = new List<ResourceAsset>();
        var sources = new List<ResourceProductionSource>();
        var baseAmounts = new List<int>();
        var upgradeLevels = new List<List<BuildingAsset.UpgradeLevel>>();
        var buildings = new List<BuildingAsset>();

        int count = _productionBuildings != null ? _productionBuildings.Length : 0;
        for (int i = 0; i < count; i++)
        {
            BuildingAsset building = _productionBuildings[i];
            if (building == null)
            {
                Debug.LogError($"[경영] {i}번 생산 건물이 비어 있습니다.");
                continue;
            }

            // 건물의 Production에서 생산처를 만든다(타입·OutputResource 검증은 TryCreate가 담당·로깅).
            if (!ResourceProductionSource.TryCreate(building, _wallet, out ResourceProductionSource source))
            {
                continue;
            }

            // ResourceAsset.Data는 호출부가 채우는 규약(SystemMap §2).
            ResourceAsset output = building.Production.OutputResource;
            if (output.Data == null && table != null)
            {
                output.Data = table.Get(output.ResourceID);
            }
            if (output.Data == null)
            {
                Debug.LogError($"[경영] '{output.ResourceID}' Data를 채우지 못해 라인 제외.");
                continue;
            }

            assets.Add(output);
            sources.Add(source);
            baseAmounts.Add(Mathf.Max(0, building.Production.BaseAmountPerVillager)); // 레벨0 주민당량
            upgradeLevels.Add(building.Production.UpgradeLevels ?? new List<BuildingAsset.UpgradeLevel>());
            buildings.Add(building);
        }

        _lineAssets = assets.ToArray();
        _sources = sources.ToArray();
        _amountPerVillager = baseAmounts.ToArray();
        _lineUpgradeLevels = upgradeLevels.ToArray();
        _lineBuildings = buildings.ToArray();
        _level = new int[_sources.Length];
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

        // 확보/낮 시작 등 영토 상태가 바뀌면 패널을 갱신한다 — 수급 row의 일일량·활성(회색) 상태 반영(#166).
        // ⚠ 확보 시 즉시 지급은 없다. 자원은 매일 정산(HandleNightToDay → SettleTerritorySupply)에서
        //   확보(Owned) 노드로부터 자동 수급된다(GDD §3.2 미개척 영지 = 매일 자동 수급).
        _territory.OnChanged += HandleTerritoryChanged;
    }

    private void HandleTerritoryChanged() => OnChanged?.Invoke();

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

    /// <summary>
    /// 생산 건물(라인)을 한 단계 업그레이드한다 — 낮에만, 다음 레벨 비용을 감당 가능할 때만.<br/>
    /// 비용은 <see cref="TrySpend"/> 게이트웨이로 원자적으로 차감하고(WL-017/WL-048), 성공 시 레벨↑·주민당량 갱신.
    /// 성공 여부를 반환한다. (UI는 다음 이슈 — 지금은 이 진입점만 제공.)
    /// </summary>
    public bool TryUpgrade(int index)
    {
        if (!IsDay)
        {
            Debug.Log("[경영] 밤에는 업그레이드할 수 없습니다.");
            return false;
        }
        if (!IsValidLine(index))
        {
            return false;
        }

        List<BuildingAsset.UpgradeLevel> levels = _lineUpgradeLevels[index];
        int next = _level[index];
        if (next >= levels.Count)
        {
            Debug.Log($"[경영] {LineDisplayName(index)}: 이미 최대 레벨입니다. (Lv{_level[index]})");
            return false;
        }

        BuildingAsset.UpgradeLevel target = levels[next];
        if (!TrySpend(target.Cost))
        {
            Debug.Log($"[경영] {LineDisplayName(index)}: 자원이 부족해 업그레이드할 수 없습니다.");
            return false;
        }

        _level[index] = next + 1;
        _amountPerVillager[index] = target.AmountPerVillager;
        Debug.Log($"[경영] {LineDisplayName(index)} 업그레이드 → Lv{_level[index]} (주민당량 {_amountPerVillager[index]})");
        OnChanged?.Invoke();
        return true;
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

            int produced = _sources[i].Produce(_villagerCounts[i], _amountPerVillager[i], ProductionMultiplier(i));
            Debug.Log($"[정산] {LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, _lineAssets[i].Data.NameKey)}: 주민 {_villagerCounts[i]}명 → +{produced}");
        }

        for (int i = 0; i < _villagerCounts.Length; i++)
        {
            _villagerCounts[i] = 0;
        }

        // 미개척 영지 일일 자동 수급(#166): 확보(Owned)한 영지가 매일 자기 자원을 DailyYield만큼 지급한다.
        // 주민 배치와 무관한 패시브 수입 — 확보한 영지 수·종류가 늘수록 총 수급이 늘어난다(GDD §3.2).
        SettleTerritorySupply();

        _wallet.Add(ResourceKind.Mana, _manaPerWaveClear);
        Debug.Log($"[정산] 웨이브 클리어 보상: 마나석 +{_manaPerWaveClear}");

        Debug.Log($"[경영] 밤 → 낮 (Wave {WaveCount}): 자원 정산 + 주민 배치 초기화");
        OnChanged?.Invoke();
    }

    // 확보(Owned)한 미개척 영지들이 매일 지급하는 자원을 지갑에 정산한다(#166). 주민 배치와 무관.
    // 본진(Definition == null)·미확보 노드는 건너뛴다.
    private void SettleTerritorySupply()
    {
        if (_territory == null || _territory.Graph == null)
        {
            return;
        }

        IReadOnlyList<TerritoryNode> nodes = _territory.Graph.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            TerritoryNode node = nodes[i];
            if (node.State != TerritoryState.Owned || node.Definition == null || node.DailyYield <= 0)
            {
                continue;
            }

            _wallet.Add(node.Definition.Kind, node.DailyYield);
            Debug.Log($"[정산] 영지 수급: {node.Definition.Kind} +{node.DailyYield} (노드 {node.Id})");
        }
    }

    /// <summary>
    /// 지정 자원의 일일 자동 수급량 합 — 확보(Owned)한 그 종류 미개척 영지들의 <c>DailyYield</c> 총합(#166).<br/>
    /// 그 종류 영지를 아직 확보하지 않았으면 0. 패널의 특수 자원 row가 "+n"·활성(회색) 판정에 쓴다.
    /// </summary>
    public int SupplyDaily(ResourceKind kind)
    {
        if (_territory == null || _territory.Graph == null)
        {
            return 0;
        }

        int sum = 0;
        IReadOnlyList<TerritoryNode> nodes = _territory.Graph.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            TerritoryNode node = nodes[i];
            if (node.State == TerritoryState.Owned && node.Definition != null && node.Definition.Kind == kind)
            {
                sum += node.DailyYield;
            }
        }
        return sum;
    }

    /// <summary>웨이브 클리어(밤→낮) 시 지급되는 마나석 고정량 — 마나 row의 "+n" 미리보기용(#166).</summary>
    public int ManaPerWaveClear => _manaPerWaveClear;

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
