using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 경영 자원 시스템의 애플리케이션 로직(UI 무관). 지갑·생산처·주민 배치 상태를 소유하고,
/// DayNightManager 전환 이벤트에 반응해 정산/초기화한다. UI(<see cref="ManagementPanelView"/>)는
/// 이 컨트롤러만 구독·호출하므로, 실제 UI 아트로 교체해도 이 클래스는 바뀌지 않는다.<br/>
/// <br/>
/// - 낮→밤(OnDayToNight): 각 생산처 Produce로 자원 정산 (팀 계약 #5)<br/>
/// - 밤→낮(OnNightToDay): 주민 배치 초기화 (팀 계약 #5)<br/>
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
    private ResourceProductionSource[] _sources;
    private ResourceAsset[] _lineAssets;
    private int[] _villagerCounts;

    private DayNightManager _dayNight;

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

    // 잉여 주민이 없어야(전원 배치) 밤으로 전환 가능.
    public bool CanEndDay => IsDay && AssignedTotal >= _maxVillagers;

    public int ResourceCount(ResourceKind kind) => _wallet != null ? _wallet.Get(kind) : 0;

    public string LineDisplayName(int index) => IsValidLine(index) ? _lineAssets[index].Data.DisplayName : "-";
    public ResourceKind LineKind(int index) => IsValidLine(index) ? _lineAssets[index].Data.Kind : default;
    public int LineVillagers(int index) => IsValidLine(index) ? _villagerCounts[index] : 0;
    public int LineExpectedProduction(int index) => _baseAmountPerVillager * LineVillagers(index);

    // 모델은 Awake에서 구축한다 — 뷰의 Start()가 어떤 순서로 돌든 라인 수가 준비돼 있도록.
    private void Awake()
    {
        BuildModel();
    }

    private void Start()
    {
        SubscribeDayNight();
        OnChanged?.Invoke();
    }

    private void OnDestroy()
    {
        if (_dayNight != null)
        {
            _dayNight.OnDayToNight -= HandleDayToNight;
            _dayNight.OnNightToDay -= HandleNightToDay;
        }
    }

    private void BuildModel()
    {
        _wallet = new ResourceWallet();

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

        _dayNight.OnDayToNight += HandleDayToNight;
        _dayNight.OnNightToDay += HandleNightToDay;
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

    // '밤으로' 버튼: 잉여 주민이 없을 때만 DayNightManager로 전환을 요청한다.
    public void RequestEndDay()
    {
        if (!CanEndDay)
        {
            Debug.Log($"[경영] 잉여 주민이 있어 밤으로 전환할 수 없습니다. (배치 {AssignedTotal}/{_maxVillagers})");
            return;
        }
        if (_dayNight == null)
        {
            Debug.LogWarning("[경영] DayNightManager가 없어 밤으로 전환할 수 없습니다.");
            return;
        }

        _dayNight.EndDay();
    }

    // ── DayNightManager 이벤트 훅 (팀 계약 #5) ──────────────────────────
    private void HandleDayToNight()
    {
        Debug.Log("[경영] 낮 → 밤: 자원 정산");
        for (int i = 0; i < _sources.Length; i++)
        {
            if (_sources[i] == null)
            {
                continue;
            }

            int produced = _sources[i].Produce(_villagerCounts[i]);
            Debug.Log($"[정산] {_lineAssets[i].Data.DisplayName}: 주민 {_villagerCounts[i]}명 → +{produced}");
        }

        OnChanged?.Invoke();
    }

    private void HandleNightToDay()
    {
        for (int i = 0; i < _villagerCounts.Length; i++)
        {
            _villagerCounts[i] = 0;
        }

        Debug.Log($"[경영] 밤 → 낮 (Wave {WaveCount}): 주민 배치 초기화");
        OnChanged?.Invoke();
    }

    private bool CanEditLine(int index)
    {
        if (!IsDay)
        {
            Debug.Log("[경영] 밤에는 배치를 변경할 수 없습니다.");
            return false;
        }
        return IsValidLine(index);
    }

    private bool IsValidLine(int index) =>
        _sources != null && index >= 0 && index < _sources.Length && _sources[index] != null;
}
