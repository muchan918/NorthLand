using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 경영 자원 시스템의 애플리케이션 로직(UI 무관). 지갑·생산처·주민 배치 상태를 소유하고,
/// DayNightManager 전환 이벤트에 반응해 정산/초기화한다. UI(<see cref="ManagementPanelView"/>)는
/// 이 컨트롤러만 구독·호출하므로, 실제 UI 아트로 교체해도 이 클래스는 바뀌지 않는다.<br/>
/// <br/>
/// - 낮→밤(OnDayToNight): 주민 배치 확정 (자원 정산 없음, 팀 계약 #5)<br/>
/// - 밤→낮(OnNightToDay): 각 생산처 Produce로 자원 정산 (주민 배치는 초기화하지 않고 유지 — #219)<br/>
/// - 주민 수·풀(maxVillagers)은 주민 시스템 부재로 임시 placeholder — 주민 시스템이 생기면 이 부분만 교체.<br/>
/// (Docs/ManagementArea/Resources.md — 이슈 #43)
/// </summary>
public class ManagementController : MonoBehaviour
{
    [Tooltip("생산 라인이 되는 생산 건물들(나무꾼의 집·광산·농장). 각 건물의 Production에서 산출 자원·주민당량·업그레이드 테이블을 읽는다.")]
    [SerializeField] BuildingAsset[] _productionBuildings;

    [Tooltip("업그레이드만 하는 건물들(마법 연구소 등). 생산 라인이 아니라 마나석으로 레벨만 올린다. " +
             "강화 효과는 소비 시스템(스킬 등)이 GetUpgradeLevel로 레벨을 읽어 적용한다(결합도 최소, 효과는 TODO).")]
    [SerializeField] BuildingAsset[] _upgradeBuildings;

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

    // 업그레이드 전용 건물(마법 연구소 등) 상태 — 생산 라인과 완전히 분리한다(주민·산출 자원·주민당량 개념 없음).
    // 생산 라인과 같은 계보로 레벨만 런타임 상태로 소유하고(공유 SO 오염 금지, WL-016), 비용 차감은 동일한 TrySpend 게이트웨이를 쓴다.
    // _upgradeLevel[i]=현재 레벨(0=미업그레이드), _upgradeLevelTables[i]=그 건물의 레벨 테이블(비용, SO에서 추출한 읽기 전용),
    // _upgradeBuildingRefs[i]=index → 원본 건물 SO(건물→인덱스 매핑용).
    private int[] _upgradeLevel;
    private List<BuildingAsset.SkillUpgradeLevel>[] _upgradeLevelTables;
    private BuildingAsset[] _upgradeBuildingRefs;

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

    // 오늘 영토를 확장했는가 — 낮 종료 확인 팝업의 경고 판정용(#219). 영토가 씬에 없으면(null) permissive(true).
    public bool HasExpandedTerritory => _territory == null || _territory.HasExpandedToday;

    // 아직 배치되지 않은(유휴) 주민이 있는가 — 낮 종료 확인 팝업의 경고 판정용(#219).
    public bool HasIdleVillagers => AssignedTotal < _maxVillagers;

    // 페이즈 전환 버튼 활성 조건(#219): DayNight가 있으면 항상 활성. 강제 게이팅을 해제했고,
    // 낮 종료 조건 미충족(영토 미확장·유휴 주민)은 버튼 비활성이 아니라 확인 팝업으로 안내한다.
    public bool CanAdvancePhase => _dayNight != null;

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

    // ── 업그레이드 전용 건물(마법 연구소 등) 조회/실행 API ─────────────────
    // 생산 라인과 별개 트랙이라 index 도메인도 별개다(UpgradeIndexOf로 얻는다). BuildingInfoUI가 바인딩한다.

    /// <summary>업그레이드 전용 건물 SO가 몇 번 index인지. 업그레이드 건물이 아니면 -1.</summary>
    public int UpgradeIndexOf(BuildingAsset building)
    {
        if (building == null || _upgradeBuildingRefs == null) return -1;
        for (int i = 0; i < _upgradeBuildingRefs.Length; i++)
        {
            if (_upgradeBuildingRefs[i] == building) return i;
        }
        return -1;
    }

    public int UpgradeBuildingLevel(int index) => IsValidUpgrade(index) ? _upgradeLevel[index] : 0;
    public int UpgradeBuildingMaxLevel(int index) => IsValidUpgrade(index) ? _upgradeLevelTables[index].Count : 0;

    // 다음 레벨(=현재 레벨 인덱스)의 비용. 최대 레벨이거나 index 무효면 null(표시부는 "MAX" 처리).
    public IReadOnlyList<ResourceCost> UpgradeBuildingCost(int index)
    {
        if (!IsValidUpgrade(index)) return null;
        List<BuildingAsset.SkillUpgradeLevel> levels = _upgradeLevelTables[index];
        int next = _upgradeLevel[index];
        return next < levels.Count ? levels[next].Cost : null;
    }

    // 업그레이드 가능 여부: 낮이어야 하고, 다음 레벨이 있어야 하고, 그 비용(마나석)을 감당할 수 있어야 한다.
    public bool CanUpgradeBuilding(int index)
    {
        if (!IsDay || !IsValidUpgrade(index)) return false;
        List<BuildingAsset.SkillUpgradeLevel> levels = _upgradeLevelTables[index];
        int next = _upgradeLevel[index];
        return next < levels.Count && CanAfford(levels[next].Cost);
    }

    /// <summary>
    /// 업그레이드 전용 건물(마법 연구소 등)을 한 단계 올린다 — 낮에만, 다음 레벨 비용을 감당 가능할 때만.<br/>
    /// 비용은 생산 건물과 동일한 <see cref="TrySpend"/> 게이트웨이로 원자적 차감(WL-017/WL-048), 성공 시 레벨↑.
    /// 강화 효과는 여기서 적용하지 않는다 — 소비 시스템이 <see cref="GetUpgradeLevel"/>로 레벨을 읽어 정한다(결합도 최소, TODO).
    /// </summary>
    public bool TryUpgradeBuilding(int index)
    {
        if (!IsDay)
        {
            Debug.Log("[경영] 밤에는 업그레이드할 수 없습니다.");
            return false;
        }
        if (!IsValidUpgrade(index))
        {
            return false;
        }

        List<BuildingAsset.SkillUpgradeLevel> levels = _upgradeLevelTables[index];
        int next = _upgradeLevel[index];
        if (next >= levels.Count)
        {
            Debug.Log($"[경영] {_upgradeBuildingRefs[index].BuildingID}: 이미 최대 레벨입니다. (Lv{_upgradeLevel[index]})");
            return false;
        }

        if (!TrySpend(levels[next].Cost))
        {
            Debug.Log($"[경영] {_upgradeBuildingRefs[index].BuildingID}: 자원이 부족해 업그레이드할 수 없습니다.");
            return false;
        }

        _upgradeLevel[index] = next + 1;
        Debug.Log($"[경영] {_upgradeBuildingRefs[index].BuildingID} 업그레이드 → Lv{_upgradeLevel[index]}");
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 업그레이드 전용 건물의 현재 레벨을 읽는다(미보유·미등록이면 0) — 소비 시스템(스킬 강화 등)이 참조하는 저결합 창구.<br/>
    /// 소비 측은 이 컨트롤러와 대상 건물 SO만 알면 되고, 레벨→효과 매핑은 소비 측이 소유한다(효과 적용은 TODO).<br/>
    /// 레벨 변경은 <see cref="OnChanged"/>로 통지되므로 소비 측은 이를 구독해 다시 pull하면 된다.
    /// </summary>
    public int GetUpgradeLevel(BuildingAsset building)
    {
        int index = UpgradeIndexOf(building);
        return index >= 0 ? _upgradeLevel[index] : 0;
    }

    private bool IsValidUpgrade(int index) =>
        _upgradeBuildingRefs != null && index >= 0 && index < _upgradeBuildingRefs.Length;

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

        BuildUpgradeBuildings();
    }

    // 업그레이드 전용 건물(마법 연구소 등) 트랙을 구축한다. 생산 라인과 달리 생산처·산출 자원이 없어
    // 레벨 테이블(비용)만 캡처하면 된다. 스킬 타입이 아니거나 레벨 테이블이 비어도 등록은 하되 최대 레벨 0으로 둔다
    // (BuildingInfoUI가 "업그레이드 불가"로 표시). 실제 강화 효과는 소비 시스템이 레벨을 참조해 정한다(TODO).
    private void BuildUpgradeBuildings()
    {
        var refs = new List<BuildingAsset>();
        var tables = new List<List<BuildingAsset.SkillUpgradeLevel>>();

        int count = _upgradeBuildings != null ? _upgradeBuildings.Length : 0;
        for (int i = 0; i < count; i++)
        {
            BuildingAsset building = _upgradeBuildings[i];
            if (building == null)
            {
                Debug.LogError($"[경영] {i}번 업그레이드 건물이 비어 있습니다.");
                continue;
            }

            refs.Add(building);
            tables.Add(building.Skill?.UpgradeLevels ?? new List<BuildingAsset.SkillUpgradeLevel>());
        }

        _upgradeBuildingRefs = refs.ToArray();
        _upgradeLevelTables = tables.ToArray();
        _upgradeLevel = new int[_upgradeBuildingRefs.Length];
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

    // 낮→밤 전환을 수행한다(#219): 강제 조건 없음 — 영토 미확장·유휴 주민이어도 넘어간다.
    // 조건 미충족 시의 확인은 UI(ManagementEndDayConfirmPopup)가 담당한다. 밤→낮(EndNight)은
    // 웨이브 성공 버튼이 전담(WL-018)하므로, 이 메서드는 밤에는 아무 동작도 하지 않는다.
    public void EndDay()
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

        // 주민 배치는 초기화하지 않고 전날 배치를 그대로 유지한다(#219) — 매일 재배치 강제를 없앤다.

        // 미개척 영지 일일 자동 수급(#166): 확보(Owned)한 영지가 매일 자기 자원을 DailyYield만큼 지급한다.
        // 주민 배치와 무관한 패시브 수입 — 확보한 영지 수·종류가 늘수록 총 수급이 늘어난다(GDD §3.2).
        SettleTerritorySupply();

        _wallet.Add(ResourceKind.Mana, _manaPerWaveClear);
        Debug.Log($"[정산] 웨이브 클리어 보상: 마나석 +{_manaPerWaveClear}");

        Debug.Log($"[경영] 밤 → 낮 (Wave {WaveCount}): 자원 정산 (주민 배치 유지, #219)");
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
        // 영토 확장 선행 조건 제거(#219) — 영토를 확장하지 않아도 주민을 배치할 수 있다(밤 게이팅만 유지).
        return IsValidLine(index);
    }

    private bool IsValidLine(int index) =>
        _sources != null && index >= 0 && index < _sources.Length && _sources[index] != null;
}
