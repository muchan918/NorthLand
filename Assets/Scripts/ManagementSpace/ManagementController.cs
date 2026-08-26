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

    // ⚠ 필드명을 바꾸지 말 것 — GameScene의 인스턴스 오버라이드(_maxVillagers: 2)가 이름으로 매칭되므로
    //   이름을 바꾸면 씬에 저장된 시작값이 유실되고 스크립트 기본값으로 되돌아간다.
    [Tooltip("게임 시작 시 보유한 주민 수(#227). 본진에서 늘린 증가분은 런타임 상태(_bonusVillagers)로 따로 들고 있고, " +
             "실제 상한은 둘을 합친 MaxVillagers다. 전 라인 배치 합계 상한.")]
    [SerializeField] int _maxVillagers = 2;

    /// <summary>
    /// 증축 주민을 제외한 게임 시작 시 기본 주민 수.
    /// </summary>
    public int BaseMaxVillagers => _maxVillagers;

    [Tooltip("웨이브 클리어(밤→낮 정산) 시 지급되는 마나석 고정량 (GDD §4.3)")]
    [SerializeField] int _manaPerWaveClear = 10;

    [Tooltip("게임 시작(런당 1회) 시 지급되는 초기 나무/철/식량. 마나석은 전투 보상 전용이라 제외(팀 계약 #3, 이슈 #130)")]
    [SerializeField] int _initialWood = 110;
    [SerializeField] int _initialIron = 40;
    [SerializeField] int _initialFood = 0;

    /// <summary>상태(자원·주민 배치·페이즈)가 바뀔 때 발생. 뷰는 이걸 구독해 다시 렌더한다.</summary>
    public event Action OnChanged;

    /// <summary>건물에 반영된 플레이어 행동의 종류 — <see cref="OnBuildingAction"/>의 페이로드.
    /// 새 연출을 붙일 땐 여기에 값을 추가하고 해당 게이트웨이에서 발화만 하면 된다(구독 측은 안 바뀐다).</summary>
    public enum BuildingAction
    {
        /// <summary>레벨이 올랐다 — 생산 라인(<see cref="TryUpgrade"/>)·업그레이드 전용 건물(<see cref="TryUpgradeBuilding"/>) 공통.</summary>
        Upgraded,

        /// <summary>본진에서 주민 수를 늘렸다(<see cref="TryIncreaseVillagers"/>).</summary>
        VillagerIncreased,

        /// <summary>이 건물에 주민 1명이 배치됐다(<see cref="AssignVillager"/>) — 마을 군중이 1명 줄어야 한다(#341, Resident.md §3.2).</summary>
        VillagerAssigned,

        /// <summary>이 건물에서 주민 1명이 빠졌다(<see cref="UnassignVillager"/>) — <b>그 건물의</b> 출입 포인트에서 1명이 걸어 나온다(#341, Resident.md §3.2).</summary>
        VillagerUnassigned,

        /// <summary>이 건물에서 자원을 교환했다(<see cref="TryExchange"/>). 어떤 offer였는지는 싣지 않는다.</summary>
        ///
        /// ⚠ 새 값은 <b>반드시 맨 끝에</b> 더한다 — BuildingActionCondition이 이 enum을 정수 인덱스로
        ///   직렬화하므로, 중간에 끼우면 기존 단계 에셋이 다른 행동을 가리키게 된다.
        Exchanged,
    }

    /// <summary>
    /// 특정 건물에 플레이어 행동이 반영됐을 때 발생 — "어느 건물(SO)에 무슨 일(<see cref="BuildingAction"/>)"만 알린다.<br/>
    /// 상태 통지인 <see cref="OnChanged"/>와 달리 <b>대상이 특정되는</b> 이벤트라, 그 건물 자리에서 재생할
    /// 연출(<c>BuildingFeedback</c>)·사운드가 구독한다. 컨트롤러는 연출을 전혀 모른다(결합도 최소, WL-016과 같은 취지).
    /// </summary>
    public event Action<BuildingAsset, BuildingAction> OnBuildingAction;

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
    // 레벨 테이블은 타입 중립 소스(BuildingAsset.UpgradeSteps)로 받는다 — 컨트롤러가 여기서 읽는 건
    // 비용과 본진 요구치뿐이라 구체 타입을 알 필요가 없다(#229 선행 작업, BuildingUpgrade.md §8).
    private int[] _upgradeLevel;
    private IReadOnlyList<BuildingAsset.UpgradeStep>[] _upgradeLevelTables;
    private BuildingAsset[] _upgradeBuildingRefs;

    // 업그레이드 트랙 중 본진의 index(없으면 -1). 하위 건물 해금·교환 배율이 전부 이 레벨 하나를 기준으로 삼는다.
    // 별도 SerializeField를 두지 않는다 — 씬 배선은 _upgradeBuildings에 본진 SO를 넣는 것 하나로 끝난다.
    private int _castleIndex = -1;

    // 본진에서 늘린 주민 수(#227). 시작값(_maxVillagers)과 분리해 런타임 상태로만 소유한다 —
    // 공유 SO(castle.asset)에 쓰면 다른 런/인스턴스까지 오염된다(WL-016, 건물 레벨과 같은 취지).
    // 이 값 자체가 곧 '소진한 비용 테이블 행 수'다 — 별도 카운터를 두면 둘이 어긋날 수 있어 하나로 겸한다.
    // 밤→낮 전환은 배치(_villagerCounts)만 초기화하므로 늘어난 주민 수는 자동으로 다음날에도 유지된다.
    private int _bonusVillagers;

    private DayNightManager _dayNight;

    // ResourceAsset.Data 채움용 지연 캐시(호출부 채움 규약, SystemMap §2).
    private ResourceTable _resourceTable;

    // 해석 불가 비용을 이미 보고한 Cost 리스트(인스턴스 참조) — ReportUnresolvableCost 참고.
    private readonly HashSet<IReadOnlyList<ResourceCost>> _reportedBadCosts = new();

    public int LineCount => _sources != null ? _sources.Length : 0;
    // 총 보유 주민 수 = 시작값 + 본진에서 늘린 증가분(#227). 소비처는 항상 이 프로퍼티를 읽는다.
    public int MaxVillagers => _maxVillagers + _bonusVillagers;
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

    // 아직 배치되지 않은(유휴) 주민이 있는가 — 낮 종료 확인 팝업의 유일한 경고 판정용(#337).
    // 기준은 시작값(_maxVillagers)이 아니라 본진 증가분을 더한 MaxVillagers다(#227) —
    // 팝업이 표시하는 유휴 인원수(MaxVillagers - AssignedTotal)와 판정 기준이 어긋나면
    // "2/5인데 경고가 안 뜬다" 같은 조용한 누락이 생긴다.
    public bool HasIdleVillagers => AssignedTotal < MaxVillagers;

    // 페이즈 전환 버튼 활성 조건(#219): DayNight가 있으면 항상 활성. 강제 게이팅을 해제했고,
    // 낮 종료 조건 미충족(유휴 주민)은 버튼 비활성이 아니라 확인 팝업으로 안내한다.
    public bool CanAdvancePhase => _dayNight != null;

    public int ResourceCount(ResourceKind kind) => _wallet != null ? _wallet.Get(kind) : 0;

    /// <summary>
    /// 자원 보유량을 지정한 절대값으로 복원한다.
    /// 획득량을 더하거나 비용을 차감하지 않고 지갑 잔액을 직접 맞춘다.
    /// </summary>
    /// <param name="kind">복원할 자원 종류.</param>
    /// <param name="amount">복원할 보유량. 0 이상이어야 한다.</param>
    /// <returns>
    /// 지갑이 준비되어 있고 값이 유효하면 true.
    /// </returns>
    public bool TryRestoreResource(ResourceKind kind,int amount)
    {
        if (_wallet == null)
        {
            Debug.LogError("[경영 복원] ResourceWallet이 준비되지 않았습니다.",this);

            return false;
        }

        return _wallet.TrySet(kind, amount);
    }

    // ── 비용 소비 게이트웨이 (소비처는 지갑에 직접 접근하지 않고 컨트롤러 경유 — WL-017) ──
    /// <summary>Cost 리스트를 감당할 수 있는지 판정한다. null/빈 리스트는 무료(true).<br/>
    /// 자원 해석에 실패하면(삭제·미배선 SO) 감당 불가로 본다 — 근거는 <see cref="TryAggregateCost"/>(WL-176).</summary>
    /// <summary>
    /// [튜토리얼용] 켜면 경영 조작이 자원을 소모하지 않는다.<br/>
    /// 생산 라인(<see cref="TryUpgrade"/>) · 업그레이드 전용 건물(<see cref="TryUpgradeBuilding"/>) ·
    /// 주민 증축(<see cref="TryIncreaseVillagers"/>)에 걸린다.<br/>
    /// 교환(<see cref="TryExchange"/>)은 빠진다 — 비용을 ResourceCost 리스트가 아니라 (자원 종류, 수량)
    /// 한 쌍으로 들고 있어 <see cref="EffectiveCost"/>가 걸릴 자리가 없다.
    /// </summary>
    public bool FreeManagementCost
    {
        get => _freeUpgrade;
        set
        {
            if (_freeUpgrade == value)
            {
                return;
            }

            _freeUpgrade = value;

            // 이 값이 바뀌면 CanUpgrade의 답이 뒤집힌다 — 열려 있는 건물 정보 패널의 업그레이드 버튼이
            // 다시 그려지지 않으면 무료로 켠 직후에도 회색으로 남는다(BuildingInfoUI가 OnChanged로 갱신한다).
            OnChanged?.Invoke();
        }
    }

    private bool _freeUpgrade;

    /// <summary>
    /// [튜토리얼용] 이 레벨을 넘겨 올릴 수 없다. 0이면 제한 없음.<br/>
    /// <see cref="FreeManagementCost"/>로 비용이 사라진 단계에서 한 건물만 계속 올리는 것을 막는다.<br/>
    /// <br/>
    /// ⚠ 그 단계의 완료 조건이 요구하는 레벨보다 낮게 잡으면 <b>단계를 영영 끝낼 수 없다</b>
    ///   (예: AllProductionLinesUpgradedCondition의 Required Level과 맞출 것).
    /// </summary>
    public int UpgradeCap
    {
        get => _upgradeCap;
        set
        {
            if (_upgradeCap == value)
            {
                return;
            }

            _upgradeCap = value;

            // FreeUpgrade와 같은 이유 — 이 값이 바뀌면 CanUpgrade의 답과 "Lv 현재/최대" 표시가 뒤집히는데,
            // 열려 있는 건물 정보 패널은 OnChanged로만 다시 그린다.
            OnChanged?.Invoke();
        }
    }

    private int _upgradeCap;

    /// <summary>
    /// [튜토리얼용] 주민을 이 인원까지만 늘릴 수 있다(보너스 기준). 0이면 제한 없음.<br/>
    /// 무료로 열어 둔 단계에서 증축을 계속 눌러 주민이 불어나는 것을 막는다 —
    /// castle.asset의 증축 레벨이 8개라 상한이 없으면 그만큼 누를 수 있다.
    /// </summary>
    public int VillagerCap
    {
        get => _villagerCap;
        set
        {
            if (_villagerCap == value)
            {
                return;
            }

            _villagerCap = value;

            // NextVillagerCost의 답이 뒤집힌다 — 본진 패널은 OnChanged로만 다시 그린다.
            OnChanged?.Invoke();
        }
    }

    private int _villagerCap;

    /// <summary>
    /// [튜토리얼용] 여기 담긴 건물만 업그레이드할 수 있다. 비우면 제한 없음.<br/>
    /// <see cref="UpgradeCap"/>이 '얼마나'를 막는다면 이쪽은 '무엇을'을 막는다 —
    /// 무료 구간에서 안내한 건물이 아닌 곳(캐슬·마법 연구소 등)으로 무료 업그레이드가 새는 것을 막는다.<br/>
    /// <br/>
    /// ⚠ 이게 없으면 앞 단계에서 뒤 단계의 대상을 미리 올려버릴 수 있고, 그러면 뒤 단계가
    ///   상한에 걸려 Upgraded 통지가 영영 오지 않아 <b>진행이 막힌다</b>.
    /// </summary>
    public IReadOnlyList<BuildingAsset> UpgradeAllowList
    {
        get => _upgradeAllowList;
        set
        {
            if (ReferenceEquals(_upgradeAllowList, value))
            {
                return;
            }

            _upgradeAllowList = value;

            // CanUpgrade의 답이 뒤집힌다 — 열려 있는 패널은 OnChanged로만 다시 그린다.
            OnChanged?.Invoke();
        }
    }

    private IReadOnlyList<BuildingAsset> _upgradeAllowList;

    // 목록이 비어 있으면 제한 없음. 담겨 있으면 그 안에 든 건물만 통과한다.
    private bool IsUpgradeAllowed(BuildingAsset building)
    {
        if (_upgradeAllowList == null || _upgradeAllowList.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < _upgradeAllowList.Count; i++)
        {
            if (_upgradeAllowList[i] == building)
            {
                return true;
            }
        }

        return false;
    }

    /// 무료 중이면 비용을 통째로 지운다. <b>감당 판정·실제 차감·되돌리기 환원이 같은 값을 봐야</b>
    /// "버튼은 켜졌는데 눌러도 안 되는"(CanUpgrade 누락)·"안 낸 자원이 Ctrl+Z로 환불되는"
    /// (PushSpendUndo 누락) 어긋남이 생기지 않는다.
    /// null은 CanAfford가 무료로 취급하고, TrySpend는 빈 집계라 아무것도 쓰지 않고 성공한다.
    private IReadOnlyList<ResourceCost> EffectiveCost(IReadOnlyList<ResourceCost> cost)
        => FreeManagementCost ? null : cost;

    public bool CanAfford(IReadOnlyList<ResourceCost> costs)
    {
        if (costs == null || costs.Count == 0) return true; // 무료 — 매 프레임 조회 시 할당 회피
        if (_wallet == null) return false;
        if (!TryAggregateCost(costs, true, out Dictionary<ResourceKind, int> needs)) return false;
        foreach (KeyValuePair<ResourceKind, int> need in needs)
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

        if (!TryAggregateCost(costs, true, out Dictionary<ResourceKind, int> needs)) return false;
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

    /// <summary><see cref="TrySpend"/>의 대칭짝. 이미 지불된 비용을 100% 되돌린다(되돌리기 #281).
    /// null/빈 리스트는 no-op.</summary>
    ///
    /// ⚠ **이 메서드는 팀 계약 #3·#6(WL-017)을 명시적으로 갱신하는 지점이다.** 여태 `_wallet.Add`를
    /// public으로 열지 않은 근거는 "차감+지급이 한 몸인 진입점만 노출한다"(<see cref="TryExchange"/> 주석)였는데,
    /// 되돌리기는 **차감이 이미 일어난 뒤의 역연산**이라 그 형태에 담기지 않는다 — 지불과 환원이
    /// 다른 시점, 다른 사용자 조작에서 일어나기 때문이다.
    /// 대신 **호출 조건**으로 계약을 지킨다: 인자는 반드시 커맨드가 들고 있는 **실지불 비용**이어야 하고,
    /// 임의 수량 지급에 써서는 안 된다. 그 용도는 여전히 `TryExchange` 같은 '한 몸' API로만 연다.
    public void Grant(IReadOnlyList<ResourceCost> costs)
    {
        if (costs == null || costs.Count == 0 || _wallet == null) return;

        // 환원은 관대하게 해석한다(strict=false, WL-176) — 여기서 전체를 실패시키면 해석 가능한
        // 자원까지 삼켜 플레이어가 이미 낸 값을 잃는다. 되돌리기는 덜 주는 쪽이 더 안 주는 쪽보다 낫다.
        TryAggregateCost(costs, false, out Dictionary<ResourceKind, int> gains);
        foreach (KeyValuePair<ResourceKind, int> gain in gains)
        {
            _wallet.Add(gain.Key, gain.Value);
        }
        // OnChanged는 부르지 않는다 — _wallet.OnChanged가 컨트롤러 OnChanged로 재발화된다(BuildModel).
        // TrySpend가 직접 부르지 않는 것과 같은 이유.
    }

    /// <summary>
    /// Cost 리스트를 (<see cref="ResourceKind"/> → 합산 수량)으로 해석한다. 수량 0 이하 항목은 "비용 없음"으로 건너뛴다.<br/>
    /// <br/>
    /// <b>strict=true(비용 판정·차감 경로)</b>: 자원 하나라도 해석되지 않으면 <b>전체를 실패</b>시킨다(WL-176).
    /// 종전처럼 그 항목만 건너뛰면 비용에서 통째로 빠져 "구매 실패"가 아니라 <b>무료 구매</b>가 되고,
    /// 비용 행이 전부 빠지면 <c>totals</c>가 비어 <see cref="CanAfford"/>가 true를 돌려준다 —
    /// 콘솔에도 안 뜨는 조용한 고장이었다. 삭제·미배선된 <c>ResourceAsset</c>이 null로 해석되는 순간 발생하며,
    /// #337에서 특수 자원 SO 4종을 지우면서 실제 트리거가 만들어졌다(그때 참조 SO를 함께 고쳐 현재 사례는 0건).<br/>
    /// <br/>
    /// <b>strict=false(환원 경로)</b>: 해석 실패 항목만 건너뛰고 나머지를 합산한다 — <see cref="Grant"/> 참고.
    /// </summary>
    private bool TryAggregateCost(IReadOnlyList<ResourceCost> costs, bool strict, out Dictionary<ResourceKind, int> totals)
    {
        totals = new Dictionary<ResourceKind, int>();
        if (costs == null) return true;

        for (int i = 0; i < costs.Count; i++)
        {
            ResourceCost cost = costs[i];
            if (cost == null || cost.Amount <= 0) continue;

            if (!TryResolveKind(cost.Resource, out ResourceKind kind))
            {
                ReportUnresolvableCost(costs, i, cost);
                if (strict)
                {
                    totals = null;
                    return false;
                }
                continue;
            }

            totals.TryGetValue(kind, out int cur);
            totals[kind] = cur + cost.Amount;
        }
        return true;
    }

    // 해석 불가 비용을 Cost 리스트당 1회만 보고한다. CanAfford는 타워 버튼 갱신·고스트 배치에서
    // 매 프레임 불릴 수 있어, 배선 실수 하나가 프레임마다 찍히면 콘솔이 통째로 묻힌다.
    // 키는 리스트 인스턴스(SO가 들고 있는 그 객체)라 같은 배선은 계속 같은 항목으로 접힌다.
    private void ReportUnresolvableCost(IReadOnlyList<ResourceCost> costs, int index, ResourceCost cost)
    {
        if (!_reportedBadCosts.Add(costs))
        {
            return;
        }

        string id = cost.Resource != null ? cost.Resource.ResourceID : "(Resource 미지정)";
        Debug.LogError($"[경영] 비용 {index}번 항목의 자원을 해석하지 못했습니다: {id} — " +
                       "이 비용이 걸린 구매는 전부 막힙니다. 건물/타워 SO의 Cost 배선을 확인하세요.");
    }

    // ResourceAsset → ResourceKind. ResourceAsset.Data는 호출부 채움 규약(SystemMap §2)이라
    // null이면 여기서 ResourceTable로 채운다. 해석 실패는 authoring 실수이므로 에러 로그를 남긴다.
    private bool TryResolveKind(ResourceAsset resource, out ResourceKind kind)
    {
        kind = default;
        if (resource == null) return false;

        if (resource.Data == null)
        {
            _resourceTable ??= DataTableManager.Get<ResourceTable>("ResourceTable");
            if (_resourceTable != null) resource.Data = _resourceTable.Get(resource.ResourceID);
        }
        if (resource.Data == null)
        {
            Debug.LogError($"[경영] 자원 '{resource.ResourceID}' Data를 채우지 못했습니다.");
            return false;
        }

        kind = resource.Data.Kind;
        return true;
    }

    public string LineDisplayName(int index) => IsValidLine(index) ? LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, _lineAssets[index].Data.NameKey) : "-";
    public ResourceKind LineKind(int index) => IsValidLine(index) ? _lineAssets[index].Data.Kind : default;
    public int LineVillagers(int index) => IsValidLine(index) ? _villagerCounts[index] : 0;
    // 패시브 생산 배율(보상 효과 등)을 반영한 예상 생산량. 정산부(HandleNightToDay)와 같은 식이어야 UI가 실제와 일치한다.
    public int LineExpectedProduction(int index) =>
        IsValidLine(index) ? Mathf.RoundToInt(_amountPerVillager[index] * LineVillagers(index) * ProductionMultiplier(index)) : 0;

    // ── 본진 레벨 · 해금 판정 (#229) ──────────────────────────────────────
    /// <summary>현재 본진 레벨(0 = 미업그레이드). 하위 건물 Max 해금·연금술사 교환 배율의 단일 기준.</summary>
    public int CastleLevel => _castleIndex >= 0 ? _upgradeLevel[_castleIndex] : 0;

    // 현재 본진 레벨에서 열려 있는 레벨 수. 앞에서부터 '연속으로' 요구치를 만족하는 데까지만 센다.
    // 첫 미충족 행에서 멈추는 이유: 잠긴 행을 건너뛰어 뒷행이 되살아나면 레벨 번호와 실제 도달 단계가
    // 어긋난다(레벨은 순차 증가여야 한다). 비단조 authoring은 BuildingAsset.OnValidate가 경고한다.
    // ignoreGate=true는 본진 자신 — 자기 요구치로 스스로 잠기는 데드락을 막는다.
    private int EffectiveMaxLevel(IReadOnlyList<BuildingAsset.UpgradeStep> levels, bool ignoreGate = false)
    {
        if (levels == null) return 0;
        if (ignoreGate) return Capped(levels.Count);

        int castle = CastleLevel;
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] == null || levels[i].RequiredCastleLevel > castle) return Capped(i);
        }
        return Capped(levels.Count);
    }

    // 튜토리얼 상한을 씌운다. 가능 판정(CanUpgrade)·실행 가드(TryUpgrade)·표시(LineMaxLevel)·
    // 미리보기(LineUpgradeCost)가 전부 EffectiveMaxLevel을 지나므로, 여기 한 곳이면 넷이 같이 맞는다.
    // Can/Try에만 걸면 패널은 "Lv 1/6"인데 버튼만 회색이라 이유가 화면에 드러나지 않는다.
    private int Capped(int max) => _upgradeCap > 0 ? Mathf.Min(max, _upgradeCap) : max;

    // 주민 증축 쪽 상한. 레벨 개수를 깎으면 NextVillagerCost가 null을 내고,
    // 본진 패널이 그것을 '최대'로 읽어 버튼을 알아서 비활성화한다(UpgradeCap이 EffectiveMaxLevel을 통해
    // 표시까지 함께 맞추는 것과 같은 구조).
    private int EffectiveVillagerLevelCount(List<BuildingAsset.VillagerGrowthLevel> levels)
        => _villagerCap > 0 ? Mathf.Min(levels.Count, _villagerCap) : levels.Count;

    // 잠긴 다음 레벨이 요구하는 본진 레벨(잠기지 않았거나 진짜 최대면 0) — 표시부의 "본진 Lv n 필요" 안내용.
    private int RequiredCastleLevelAt(IReadOnlyList<BuildingAsset.UpgradeStep> levels, int current, int effectiveMax)
    {
        if (levels == null || current != effectiveMax || effectiveMax >= levels.Count) return 0;
        return levels[effectiveMax]?.RequiredCastleLevel ?? 0;
    }

    // ── 건물 업그레이드 조회 API (다음 이슈의 UI가 바인딩할 계약) ──────────
    public int LineLevel(int index) => IsValidLine(index) ? _level[index] : 0;
    // 실질 Max — 행 수가 아니라 '본진 레벨로 열려 있는 만큼'이다(#229).
    public int LineMaxLevel(int index) => IsValidLine(index) ? EffectiveMaxLevel(_lineUpgradeLevels[index]) : 0;
    public int LineAmountPerVillager(int index) => IsValidLine(index) ? _amountPerVillager[index] : 0;

    /// <summary>이 라인의 다음 레벨이 본진 레벨 부족으로 잠겨 있으면 필요한 본진 레벨, 아니면 0(#229).<br/>
    /// ⚠ 반환값은 <see cref="CastleLevel"/>과 같은 <b>내부</b> 도메인(0 = 미업그레이드)이다 — 화면에 쓸 땐 +1.</summary>
    public int LineRequiredCastleLevel(int index) =>
        IsValidLine(index) ? RequiredCastleLevelAt(_lineUpgradeLevels[index], _level[index], LineMaxLevel(index)) : 0;

    // 다음 레벨(=현재 레벨 인덱스)의 비용. 최대 레벨이거나 라인 무효면 null(표시부는 "MAX" 처리).
    public IReadOnlyList<ResourceCost> LineUpgradeCost(int index)
    {
        if (!IsValidLine(index)) return null;
        List<BuildingAsset.UpgradeLevel> levels = _lineUpgradeLevels[index];
        int next = _level[index];
        return next < EffectiveMaxLevel(levels) ? levels[next].Cost : null;
    }

    // 업그레이드 가능 여부: 낮이어야 하고, 다음 레벨이 열려 있어야 하고, 그 비용을 감당할 수 있어야 한다.
    public bool CanUpgrade(int index)
    {
        if (!IsDay || !IsValidLine(index)) return false;
        List<BuildingAsset.UpgradeLevel> levels = _lineUpgradeLevels[index];
        int next = _level[index];
        return IsUpgradeAllowed(_lineBuildings[index])
               && next < EffectiveMaxLevel(levels)
               && CanAfford(EffectiveCost(levels[next].Cost));
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
        return next < EffectiveMaxLevel(levels) ? levels[next].AmountPerVillager : _amountPerVillager[index];
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

    public int UpgradeBuildingCount =>_upgradeBuildingRefs != null? _upgradeBuildingRefs.Length: 0;

    public int UpgradeBuildingLevel(int index) => IsValidUpgrade(index) ? _upgradeLevel[index] : 0;
    // 실질 Max — 행 수가 아니라 '본진 레벨로 열려 있는 만큼'이다(#229). 본진 자신은 게이팅에서 제외한다.
    public int UpgradeBuildingMaxLevel(int index) =>
        IsValidUpgrade(index) ? EffectiveMaxLevel(_upgradeLevelTables[index], index == _castleIndex) : 0;

    /// <summary>이 건물의 다음 레벨이 본진 레벨 부족으로 잠겨 있으면 필요한 본진 레벨, 아니면 0(#229).<br/>
    /// ⚠ 반환값은 <see cref="CastleLevel"/>과 같은 <b>내부</b> 도메인(0 = 미업그레이드)이다 — 화면에 쓸 땐 +1.</summary>
    public int UpgradeBuildingRequiredCastleLevel(int index) =>
        IsValidUpgrade(index)
            ? RequiredCastleLevelAt(_upgradeLevelTables[index], _upgradeLevel[index], UpgradeBuildingMaxLevel(index))
            : 0;

    // 다음 레벨(=현재 레벨 인덱스)의 비용. 최대 레벨이거나 index 무효면 null(표시부는 "MAX" 처리).
    public IReadOnlyList<ResourceCost> UpgradeBuildingCost(int index)
    {
        if (!IsValidUpgrade(index)) return null;
        IReadOnlyList<BuildingAsset.UpgradeStep> levels = _upgradeLevelTables[index];
        int next = _upgradeLevel[index];
        return next < UpgradeBuildingMaxLevel(index) ? levels[next].Cost : null;
    }

    // 업그레이드 가능 여부: 낮이어야 하고, 다음 레벨이 열려 있어야 하고, 그 비용(마나석)을 감당할 수 있어야 한다.
    public bool CanUpgradeBuilding(int index)
    {
        if (!IsDay || !IsValidUpgrade(index)) return false;
        IReadOnlyList<BuildingAsset.UpgradeStep> levels = _upgradeLevelTables[index];
        int next = _upgradeLevel[index];
        return IsUpgradeAllowed(_upgradeBuildingRefs[index])
               && next < UpgradeBuildingMaxLevel(index)
               && CanAfford(EffectiveCost(levels[next].Cost));
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

        if (!IsUpgradeAllowed(_upgradeBuildingRefs[index]))
        {
            Debug.Log($"[경영] {_upgradeBuildingRefs[index].BuildingID}: 지금은 업그레이드할 수 없습니다.");
            return false;
        }

        IReadOnlyList<BuildingAsset.UpgradeStep> levels = _upgradeLevelTables[index];
        int next = _upgradeLevel[index];
        if (next >= UpgradeBuildingMaxLevel(index))
        {
            // 잠금(본진 레벨 부족)과 진짜 최대를 구분해 로그를 남긴다 — 왜 못 올리는지가 로그만으로 드러나야 한다.
            // 본진 레벨은 표시 규약(+1)에 맞춰 찍는다 — 로그와 화면이 다른 수를 말하면 추적이 어긋난다.
            int required = UpgradeBuildingRequiredCastleLevel(index);
            Debug.Log(required > 0
                ? $"[경영] {_upgradeBuildingRefs[index].BuildingID}: 다음 레벨은 본진 Lv{required + 1}부터 열립니다. (현재 본진 Lv{CastleLevel + 1})"
                : $"[경영] {_upgradeBuildingRefs[index].BuildingID}: 이미 최대 레벨입니다. (Lv{_upgradeLevel[index]})");
            return false;
        }

        IReadOnlyList<ResourceCost> paid = EffectiveCost(levels[next].Cost);
        if (!TrySpend(paid))
        {
            Debug.Log($"[경영] {_upgradeBuildingRefs[index].BuildingID}: 자원이 부족해 업그레이드할 수 없습니다.");
            return false;
        }

        _upgradeLevel[index] = next + 1;
        Debug.Log($"[경영] {_upgradeBuildingRefs[index].BuildingID} 업그레이드 → Lv{_upgradeLevel[index]}");

        // 되돌리기 등록(#444). 이전 레벨로 되맞추는 일은 세이브 복원 API가 그대로 해 준다.
        int previousLevel = next;
        string buildingId = UpgradeBuildingId(index);
        PushSpendUndo(paid,
            () => TryRevertUpgradeBuilding(buildingId, index, previousLevel),
            $"{_upgradeBuildingRefs[index].BuildingID} 업그레이드");
        OnChanged?.Invoke();
        OnBuildingAction?.Invoke(_upgradeBuildingRefs[index], BuildingAction.Upgraded);
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

    // ── 주민 수 증가 게이트웨이 (본진, #227) ──────────────────────────────
    // 업그레이드 트랙(_upgradeBuildings)과 같은 계보지만 별도 index 도메인을 만들지 않는다 —
    // 본진은 씬에 하나뿐이라 배열 등록·와이어링 없이 BuildingAsset을 그대로 받는 편이 결합도가 낮다
    // (GetUpgradeLevel(BuildingAsset)과 같은 형태). 비용은 동일한 TrySpend 게이트웨이를 쓴다.

    /// <summary>주민을 늘릴 수 있는 총 횟수 = 비용 테이블 행 수. 상한을 별도 필드로 두지 않고
    /// 행 수로 표현하므로(시작 2명 + 8행 = 최대 10명) 상한과 비용이 어긋날 수 없다.</summary>
    public int VillagerGrowthSteps(BuildingAsset building) => VillagerLevels(building)?.Count ?? 0;

    /// <summary>지금까지 늘린 횟수(= 소진한 행 수). 표시부가 "n/8" 같은 진행도를 그릴 때 쓴다.</summary>
    public int VillagerGrowthCount => _bonusVillagers;

    /// <summary>다음 회차 주민 증가 비용. 행을 모두 소진했거나 테이블이 없으면 null(표시부는 "MAX" 처리).</summary>
    /// ⚠ 반환하는 것은 <b>실제 비용</b>이다 — 무료 여부를 여기서 지우면 안 된다.
    /// null은 "더 늘릴 수 없다"는 신호로 쓰이고(본진 패널이 버튼을 '최대'로 바꾼다),
    /// 무료라고 null을 내면 최대에 도달한 것으로 오독된다. 무료 처리는 CanIncreaseVillagers·
    /// TryIncreaseVillagers가 EffectiveCost로 따로 한다.
    public IReadOnlyList<ResourceCost> NextVillagerCost(BuildingAsset building)
    {
        List<BuildingAsset.VillagerGrowthLevel> levels = VillagerLevels(building);
        if (levels == null || _bonusVillagers >= EffectiveVillagerLevelCount(levels)) return null;

        BuildingAsset.VillagerGrowthLevel next = levels[_bonusVillagers];
        return next?.Cost;
    }

    /// <summary>주민을 늘릴 수 있는지 — 낮이어야 하고, 남은 회차가 있어야 하고, 그 비용을 감당할 수 있어야 한다.</summary>
    public bool CanIncreaseVillagers(BuildingAsset building)
    {
        if (!IsDay) return false;
        // cost != null은 '아직 늘릴 여지가 있는가'이고, 감당 판정은 무료 여부를 태워서 본다.
        // 둘을 한 값으로 합치면 무료일 때 '최대 도달'로 오독된다(NextVillagerCost 주석 참고).
        IReadOnlyList<ResourceCost> cost = NextVillagerCost(building);
        return cost != null && CanAfford(EffectiveCost(cost));
    }

    /// <summary>
    /// 총 보유 주민 수를 1 늘린다 — 낮에만, 다음 회차 비용을 감당 가능할 때만.<br/>
    /// 비용은 건물 업그레이드와 동일한 <see cref="TrySpend"/> 게이트웨이로 원자적 차감(WL-017/WL-048),
    /// 성공 시 <see cref="MaxVillagers"/>가 즉시 올라가고 <see cref="OnChanged"/>로 뷰에 통지된다.<br/>
    /// 늘어난 주민은 그날 바로 배치할 수 있고(<see cref="AssignVillager"/>의 상한이 <see cref="MaxVillagers"/>다),
    /// 다음날에도 유지된다. 그날 배치하지 않아도 밤으로 넘어갈 수 있으며(#219로 강제 게이팅 해제),
    /// 유휴로 남으면 낮 종료 확인 팝업이 <see cref="HasIdleVillagers"/>로 경고만 한다.
    /// </summary>
    public bool TryIncreaseVillagers(BuildingAsset building)
    {
        if (!IsDay)
        {
            Debug.Log("[경영] 밤에는 주민을 늘릴 수 없습니다.");
            return false;
        }

        List<BuildingAsset.VillagerGrowthLevel> levels = VillagerLevels(building);
        if (levels == null)
        {
            return false;
        }
        if (_bonusVillagers >= EffectiveVillagerLevelCount(levels))
        {
            Debug.Log($"[경영] 주민 수가 이미 최대입니다. ({MaxVillagers}명)");
            return false;
        }

        BuildingAsset.VillagerGrowthLevel target = levels[_bonusVillagers];
        IReadOnlyList<ResourceCost> paid = target != null ? EffectiveCost(target.Cost) : null;
        if (target == null || !TrySpend(paid))
        {
            Debug.Log($"[경영] 자원이 부족해 주민을 늘릴 수 없습니다. ({_bonusVillagers + 1}회차)");
            return false;
        }

        _bonusVillagers++;
        Debug.Log($"[경영] 주민 수 증가 → {MaxVillagers}명 ({_bonusVillagers}/{levels.Count}회차)");

        // 되돌리기 등록(#444). 상한을 내리는 조작이라 되돌리기가 배치까지 볼 수 있어야 한다 —
        // 규칙은 RevertVillagerGrowth 참고.
        int previousBonus = _bonusVillagers - 1;
        PushSpendUndo(paid, () => RevertVillagerGrowth(previousBonus),
            $"{building.BuildingID} 주민 증축");
        OnChanged?.Invoke();
        OnBuildingAction?.Invoke(building, BuildingAction.VillagerIncreased);
        return true;
    }

    // 주민 증가 비용 테이블을 꺼낸다. 건물이 없거나 테이블이 비면 null — 호출부가 전부 null 가드를 탄다.
    private static List<BuildingAsset.VillagerGrowthLevel> VillagerLevels(BuildingAsset building)
    {
        if (building == null || building.Villager == null) return null;
        List<BuildingAsset.VillagerGrowthLevel> levels = building.Villager.Levels;
        return levels != null && levels.Count > 0 ? levels : null;
    }

    // ── 자원 교환 게이트웨이 (연금술사의 집, #211) ────────────────────────
    // 마나석 → 다른 자원 단방향 교환. 지갑에 '획득' 경로를 여는 유일한 소비자 대면 API이므로
    // Add를 public으로 열지 않고 '차감+지급이 한 몸인' 진입점만 노출한다(팀 계약 #3·#6, WL-017).
    // 교환 자체가 제2 자원 획득 경로라 팀 합의가 필요했던 지점 — GDD §3.2·WatchList WL-042 참고.

    /// <summary>교환 가능 여부 — 낮이어야 하고, offer가 유효하고, 지불 자원을 감당할 수 있어야 한다.</summary>
    public bool CanExchange(BuildingAsset building, BuildingAsset.ExchangeOffer offer)
    {
        if (!IsDay || _wallet == null) return false;
        if (!TryResolveExchange(building, offer, out ResourceKind payKind, out _)) return false;

        return _wallet.CanAfford(payKind, offer.PayAmount);
    }

    /// <summary>
    /// 지불 자원을 차감하고 대상 자원을 지급한다 — 낮에만, 지불량을 감당할 수 있을 때만.<br/>
    /// 차감에 실패하면 아무것도 지급하지 않는다(원자적). 성공 시 <see cref="OnChanged"/>로 통지한다.
    /// </summary>
    public bool TryExchange(BuildingAsset building, BuildingAsset.ExchangeOffer offer)
    {
        if (!IsDay)
        {
            Debug.Log("[경영] 밤에는 교환할 수 없습니다.");
            return false;
        }
        if (_wallet == null || !TryResolveExchange(building, offer, out ResourceKind payKind, out ResourceKind gainKind))
        {
            return false;
        }

        if (!_wallet.CanAfford(payKind, offer.PayAmount))
        {
            Debug.Log($"[경영] {payKind}이(가) 부족해 교환할 수 없습니다. (필요 {offer.PayAmount}, 보유 {_wallet.Get(payKind)})");
            return false;
        }
        _wallet.TrySpend(payKind, offer.PayAmount);

        int gained = ExchangeGainAmount(building, offer);
        _wallet.Add(gainKind, gained);
        Debug.Log($"[경영] 교환: {payKind} -{offer.PayAmount} → {gainKind} +{gained}");
        OnChanged?.Invoke();
        OnBuildingAction?.Invoke(building, BuildingAction.Exchanged);
        return true;
    }

    // 교환 행의 지불/획득 자원 종류를 해석한다. authoring이 불완전하면(자원 미지정·수량 0 이하) false.
    // 수량 검증까지 여기서 하는 이유: 무료 교환·0 지급이 조용히 성공하는 걸 막기 위해서다
    // (BuildingAsset.ValidateExchangeOffers가 에디터에서 경고하는 것과 같은 조건의 런타임 방어선).
    private bool TryResolveExchange(BuildingAsset building, BuildingAsset.ExchangeOffer offer,
        out ResourceKind payKind, out ResourceKind gainKind)
    {
        payKind = default;
        gainKind = default;

        if (building == null || building.Exchange == null || offer == null) return false;
        if (offer.PayAmount <= 0 || offer.GainAmount <= 0) return false;

        return TryResolveKind(building.Exchange.PayResource, out payKind)
            && TryResolveKind(offer.GainResource, out gainKind);
    }

    /// <summary>
    /// 교환으로 실제 받는 수량 — 본진 레벨에 따른 교환 효율 배율이 반영된 값(#229).<br/>
    /// 표시부(<see cref="StorePanelUI"/>)가 원본 <c>offer.GainAmount</c> 대신 이걸 보여줘야 표시와 실지급이 일치한다.<br/>
    /// <br/>
    /// 이 축만 <c>RequiredCastleLevel</c>의 의미가 다르다 — 다른 트랙에선 '이 레벨이 열린다'지만
    /// 연금술사에겐 '본진 몇 레벨부터 이 배율'이다(자체 업그레이드 버튼이 없어 레벨 개념 자체가 없다).
    /// 그래서 index 클램프가 아니라 요구치를 만족하는 마지막 행을 고른다.
    /// </summary>
    public int ExchangeGainAmount(BuildingAsset building, BuildingAsset.ExchangeOffer offer)
    {
        if (building == null || offer == null) return 0;

        List<BuildingAsset.ExchangeUpgradeLevel> levels = building.Exchange?.UpgradeLevels;
        if (levels == null || levels.Count == 0) return offer.GainAmount;

        int castle = CastleLevel;
        float multiplier = 1f;
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] == null || levels[i].RequiredCastleLevel > castle) continue;
            multiplier = levels[i].GainMultiplier;
        }
        if (multiplier <= 0f) return offer.GainAmount; // 배율 미authoring(0) 방어 — SkillManager.PositiveOr1과 같은 취지

        return Mathf.Max(1, Mathf.RoundToInt(offer.GainAmount * multiplier));
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
        OnChanged?.Invoke();
    }

    private void OnDestroy()
    {
        if (_dayNight != null)
        {
            _dayNight.OnNightToDay -= HandleNightToDay;
            _dayNight.OnDayToNight -= HandleDayToNight;
            _dayNight.OnDayStart -= HandleDayStart;
        }
    }

    private void BuildModel()
    {
        _wallet = new ResourceWallet();
        // 지갑 잔액이 바뀌면(획득·차감) 컨트롤러 OnChanged로 재발화 → 패널/HUD가 갱신된다.
        // (지갑 직접 변경엔 원래 OnChanged가 안 돌던 지점을 여기서 메운다)
        _wallet.OnChanged += (_, _) => OnChanged?.Invoke();

        // 게임 시작 초기 자원 지급(런당 1회, 이슈 #130) — 유일 창구인 ResourceWallet.Add로만 지급한다(팀 계약 #3).
        // TutorialTest3의 startOnPlay도 실제 튜토리얼과 같은 초기값을 써야 디버그 결과가 갈리지 않는다.
        TutorialController tutorial = FindFirstObjectByType<TutorialController>();
        bool tutorialRun = TutorialMode.IsActive || (tutorial != null && tutorial.StartsOnPlay);

        int initialWood = tutorialRun ? TutorialMode.InitialBiscuit : _initialWood;
        int initialIron = tutorialRun ? 0 : _initialIron;
        int initialFood = tutorialRun ? 0 : _initialFood;

        _wallet.Add(ResourceKind.Wood, initialWood);
        _wallet.Add(ResourceKind.Iron, initialIron);
        _wallet.Add(ResourceKind.Food, initialFood);
        Debug.Log($"[경영] 초기 자원 지급: Wood +{initialWood}, Iron +{initialIron}, Food +{initialFood}");

        // 생산 배율 레지스트리는 지갑과 함께 런마다 새로 만든다(패시브 생산 효과가 여기에 누적).
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
        var tables = new List<IReadOnlyList<BuildingAsset.UpgradeStep>>();
        _castleIndex = -1;

        int count = _upgradeBuildings != null ? _upgradeBuildings.Length : 0;
        for (int i = 0; i < count; i++)
        {
            BuildingAsset building = _upgradeBuildings[i];
            if (building == null)
            {
                Debug.LogError($"[경영] {i}번 업그레이드 건물이 비어 있습니다.");
                continue;
            }

            // 본진은 '데이터 존재'로 식별한다(BuildingType 분기 금지 — BuildingInfo.OnSelected 계보).
            if (building.Castle != null && building.Castle.UpgradeLevels != null && building.Castle.UpgradeLevels.Count > 0)
            {
                if (_castleIndex >= 0)
                {
                    Debug.LogError($"[경영] 본진 업그레이드 테이블을 가진 건물이 둘 이상입니다({refs[_castleIndex].BuildingID}, {building.BuildingID}) — 해금 기준이 하나여야 합니다. 먼저 등록된 쪽을 씁니다.");
                }
                else
                {
                    _castleIndex = refs.Count;
                }
            }

            refs.Add(building);
            tables.Add(building.UpgradeSteps);
        }

        _upgradeBuildingRefs = refs.ToArray();
        _upgradeLevelTables = tables.ToArray();
        _upgradeLevel = new int[_upgradeBuildingRefs.Length];

        WarnUnreachableCastleRequirements();
    }

    // 본진 테이블로 도달 불가능한 요구치를 Play 시작 시 1회 경고한다(#229).
    // BuildingAsset.OnValidate는 SO 하나만 보므로 이 교차 검증을 할 수 없다 — 본진 최대 레벨을 알려면
    // 다른 SO를 참조해야 하고 SO는 서로를 모르기 때문. 등록이 끝난 이 시점에만 판정할 수 있다.
    private void WarnUnreachableCastleRequirements()
    {
        int castleMax = _castleIndex >= 0 ? _upgradeLevelTables[_castleIndex].Count : 0;

        for (int i = 0; i < _upgradeBuildingRefs.Length; i++)
        {
            if (i == _castleIndex) continue;
            WarnIfUnreachable(_upgradeBuildingRefs[i], _upgradeLevelTables[i], castleMax);
        }
        for (int i = 0; i < _lineBuildings.Length; i++)
        {
            WarnIfUnreachable(_lineBuildings[i], _lineUpgradeLevels[i], castleMax);
        }
    }

    private static void WarnIfUnreachable(BuildingAsset building, IReadOnlyList<BuildingAsset.UpgradeStep> levels, int castleMax)
    {
        if (building == null || levels == null) return;

        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != null && levels[i].RequiredCastleLevel > castleMax)
            {
                Debug.LogWarning($"[경영] {building.BuildingID}: Lv{i + 1}의 요구 본진 레벨({levels[i].RequiredCastleLevel})이 본진 최대 레벨({castleMax})을 넘어 영원히 열리지 않습니다.");
            }
        }
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
        _dayNight.OnDayToNight += HandleDayToNight;
        _dayNight.OnDayStart += HandleDayStart;
    }

    // ── 뷰(또는 후속 패널 버튼)가 호출하는 진입점 ─────────────────────────
    /// <summary>
    /// 이 라인에 주민 1명을 배치한다 — 낮에만, 유휴 주민이 남아 있을 때만.<br/>
    /// <br/>
    /// <b>성공 여부를 반환한다</b>(#341, Resident.md §9 선행 조율). 드롭 배치(§8)는 실패를 알아야
    /// "배치 없이 그 자리에 놓기 + 거절 피드백"을 할 수 있는데, 종전 <c>void</c> 시그니처로는
    /// 감당 가능 여부를 호출부가 다시 계산해야 했다 — 같은 판정이 두 곳에 생기면 조용히 어긋난다.<br/>
    /// <br/>
    /// 성공하면 <see cref="OnBuildingAction"/>으로 <see cref="BuildingAction.VillagerAssigned"/>를 알린다.
    /// 군중(<c>ResidentSpawner</c>)이 이 통지를 받아 화면의 주민 1명을 거둔다 — 컨트롤러는 군중을 모른다.
    /// </summary>
    public bool AssignVillager(int index)
    {
        if (!CanEditLine(index))
        {
            return false;
        }
        if (AssignedTotal >= MaxVillagers)
        {
            Debug.Log($"[경영] 가용 주민이 없습니다. (배치 {AssignedTotal}/{MaxVillagers})");
            return false;
        }

        _villagerCounts[index]++;
        OnChanged?.Invoke();
        OnBuildingAction?.Invoke(_lineBuildings[index], BuildingAction.VillagerAssigned);
        return true;
    }

    /// <summary>
    /// 이 라인에서 주민 1명을 뺀다 — 낮에만, 배치된 인원이 있을 때만. 성공 여부를 반환한다.<br/>
    /// 성공하면 <see cref="OnBuildingAction"/>으로 <see cref="BuildingAction.VillagerUnassigned"/>를 알린다 —
    /// <b>대상이 특정되는</b> 이벤트라야 군중이 <b>그 건물</b>의 출입 포인트에서 1명을 내보낼 수 있다(§3.2).
    /// </summary>
    public bool UnassignVillager(int index)
    {
        if (!CanEditLine(index))
        {
            return false;
        }
        if (_villagerCounts[index] <= 0)
        {
            return false;
        }

        _villagerCounts[index]--;
        OnChanged?.Invoke();
        OnBuildingAction?.Invoke(_lineBuildings[index], BuildingAction.VillagerUnassigned);
        return true;
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

        if (!IsUpgradeAllowed(_lineBuildings[index]))
        {
            Debug.Log($"[경영] {LineDisplayName(index)}: 지금은 업그레이드할 수 없습니다.");
            return false;
        }

        List<BuildingAsset.UpgradeLevel> levels = _lineUpgradeLevels[index];
        int next = _level[index];
        if (next >= EffectiveMaxLevel(levels))
        {
            // 잠금(본진 레벨 부족)과 진짜 최대를 구분해 로그를 남긴다. 본진 레벨은 표시 규약(+1)에 맞춘다.
            int required = LineRequiredCastleLevel(index);
            Debug.Log(required > 0
                ? $"[경영] {LineDisplayName(index)}: 다음 레벨은 본진 Lv{required + 1}부터 열립니다. (현재 본진 Lv{CastleLevel + 1})"
                : $"[경영] {LineDisplayName(index)}: 이미 최대 레벨입니다. (Lv{_level[index]})");
            return false;
        }

        BuildingAsset.UpgradeLevel target = levels[next];
        IReadOnlyList<ResourceCost> paid = EffectiveCost(target.Cost);
        if (!TrySpend(paid))
        {
            Debug.Log($"[경영] {LineDisplayName(index)}: 자원이 부족해 업그레이드할 수 없습니다.");
            return false;
        }

        _level[index] = next + 1;
        _amountPerVillager[index] = target.AmountPerVillager;
        Debug.Log($"[경영] {LineDisplayName(index)} 업그레이드 → Lv{_level[index]} (주민당량 {_amountPerVillager[index]})");

        // 되돌리기 등록(#444). 복원 API가 레벨과 주민 배치를 함께 받으므로 배치 수는 스냅샷하지 않고
        // **되돌리는 시점의 값**을 읽어 그대로 유지한다 — 그 사이 주민을 넣거나 뺐을 수 있다.
        int previousLevel = next;
        string buildingId = LineBuildingId(index);
        PushSpendUndo(paid,
            () => TryRevertProductionLine(buildingId, index, previousLevel),
            $"{LineDisplayName(index)} 업그레이드");
        OnChanged?.Invoke();
        OnBuildingAction?.Invoke(_lineBuildings[index], BuildingAction.Upgraded);
        return true;
    }

    // 낮→밤 전환을 수행한다(#219): 강제 조건 없음 — 유휴 주민이 남아 있어도 넘어간다.
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
    // 낮→밤은 배치 확정만 하고 정산은 없다(팀 계약 #5) — IsDay 게이트를 쓰는 버튼(교환/업그레이드)이
    // 전환 즉시 비활성화되도록 OnChanged만 재발행한다(WL-104).
    private void HandleDayToNight() => OnChanged?.Invoke();

    // OnDayStart는 낮이 시작되는 모든 시점(1일차 부트스트랩·EndNight·SkipDay)에 발행된다.
    // OnNightToDay만 구독하면 SkipDay처럼 밤을 거치지 않고 WaveCount만 오르는 경로에서
    // 웨이브 표시가 갱신되지 않는다(ManagementPanelView._phaseText가 CurrentWave를 pull하므로).
    private void HandleDayStart() => OnChanged?.Invoke();

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

        _wallet.Add(ResourceKind.Mana, _manaPerWaveClear);
        Debug.Log($"[정산] 웨이브 클리어 보상: 마나석 +{_manaPerWaveClear}");

        Debug.Log($"[경영] 밤 → 낮 (Wave {WaveCount}): 자원 정산 (주민 배치 유지, #219)");
        OnChanged?.Invoke();
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
        return IsValidLine(index);
    }

    private bool IsValidLine(int index) =>
        _sources != null && index >= 0 && index < _sources.Length && _sources[index] != null;


    /// <summary>
    /// 생산 라인 인덱스에 대응하는 안정적인 BuildingID를 반환한다.
    /// 인덱스가 유효하지 않거나 건물이 없으면 null을 반환한다.
    /// </summary>
    public string LineBuildingId(int index)
    {
        if (!IsValidLine(index))
            return null;

        return _lineBuildings[index]?.BuildingID;
    }

    /// <summary>
    /// 업그레이드 건물 인덱스에 대응하는 안정적인 BuildingID를 반환한다.
    /// 인덱스가 유효하지 않거나 건물이 없으면 null을 반환한다.
    /// </summary>
    public string UpgradeBuildingId(int index)
    {
        if (!IsValidUpgrade(index))
            return null;

        return _upgradeBuildingRefs[index]?.BuildingID;
    }

    /// <summary>
    /// 저장된 BuildingID에 대응하는 현재 생산 라인 인덱스를 찾는다.
    /// 인스펙터 배열 순서가 변경돼도 ID를 기준으로 복원하기 위한 검색 함수다.
    /// </summary>
    /// <param name="buildingId">찾을 생산 건물 ID.</param>
    /// <returns>일치하는 인덱스, 찾지 못하면 -1.</returns>
    private int FindLineIndexById(string buildingId)
    {
        if (string.IsNullOrEmpty(buildingId) ||
            _lineBuildings == null)
        {
            return -1;
        }

        for (int i = 0; i < _lineBuildings.Length; i++)
        {
            BuildingAsset building = _lineBuildings[i];

            if (building != null && string.Equals(building.BuildingID,buildingId,StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 저장된 BuildingID에 대응하는 현재 업그레이드 건물 인덱스를 찾는다.
    /// </summary>
    /// <param name="buildingId">찾을 업그레이드 건물 ID.</param>
    /// <returns>일치하는 인덱스, 찾지 못하면 -1.</returns>
    private int FindUpgradeIndexById(string buildingId)
    {
        if (string.IsNullOrEmpty(buildingId) || _upgradeBuildingRefs == null)
        {
            return -1;
        }

        for (int i = 0;i < _upgradeBuildingRefs.Length;i++)
        {
            BuildingAsset building = _upgradeBuildingRefs[i];

            if (building != null && string.Equals(building.BuildingID,buildingId,StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// BuildingID를 기준으로 생산 라인의 레벨과 주민 배치를 복원한다.
    /// 비용 차감과 낮 여부 검사를 거치지 않는다.
    /// </summary>
    /// <param name="buildingId">복원할 생산 건물 ID.</param>
    /// <param name="level">복원할 업그레이드 레벨.</param>
    /// <param name="villagers">복원할 배치 주민 수.</param>
    /// <returns>
    /// 건물이 존재하고 값이 유효해 복원됐으면 true.
    /// </returns>
    public bool TryRestoreProductionLine(string buildingId,int level,int villagers)
    {
        if (!CanRestoreProductionLine(buildingId,level,villagers))
        {
            return false;
        }

        int index = FindLineIndexById(buildingId);

        List<BuildingAsset.UpgradeLevel> levels = _lineUpgradeLevels[index];

        _level[index] = level;
        _villagerCounts[index] = villagers;

        if (level == 0)
        {
            _amountPerVillager[index] =Mathf.Max(0,_lineBuildings[index].Production.BaseAmountPerVillager);
        }
        else
        {
            _amountPerVillager[index] =Mathf.Max(0,levels[level - 1].AmountPerVillager);
        }

        OnChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// 생산 건물 복원 값이 현재 데이터에서 유효한지 검사한다.
    /// 런타임 상태는 변경하지 않는다.
    /// </summary>
    /// <param name="buildingId">검사할 생산 건물 ID.</param>
    /// <param name="level">검사할 업그레이드 레벨.</param>
    /// <param name="villagers">검사할 배치 주민 수.</param>
    /// <returns>건물이 존재하고 값이 유효하면 true.</returns>
    public bool CanRestoreProductionLine(string buildingId,int level,int villagers)
    {
        int index = FindLineIndexById(buildingId);

        if (index < 0)
        {
            Debug.LogWarning($"[경영 복원] 생산 건물을 찾을 수 없습니다: {buildingId}",this);

            return false;
        }

        if (level < 0)
        {
            Debug.LogError($"[경영 복원] 생산 건물 레벨이 음수입니다: {buildingId}={level}",this);

            return false;
        }

        if (villagers < 0)
        {
            Debug.LogError($"[경영 복원] 배치 주민 수가 음수입니다: {buildingId}={villagers}",this);

            return false;
        }

        List<BuildingAsset.UpgradeLevel> levels = _lineUpgradeLevels[index];

        int maxLevel = levels != null ? levels.Count : 0;

        if (level > maxLevel)
        {
            Debug.LogError($"[경영 복원] 생산 건물 레벨이 현재 최대치를 초과합니다: {buildingId}={level}, 최대={maxLevel}",this);

            return false;
        }

        return true;
    }

    /// <summary>
    /// 업그레이드 건물 복원 값이 현재 데이터에서 유효한지 검사한다.
    /// 런타임 상태는 변경하지 않는다.
    /// </summary>
    /// <param name="buildingId">검사할 업그레이드 건물 ID.</param>
    /// <param name="level">검사할 업그레이드 레벨.</param>
    /// <returns>건물이 존재하고 레벨이 유효하면 true.</returns>
    public bool CanRestoreUpgradeBuilding(string buildingId,int level)
    {
        int index = FindUpgradeIndexById(buildingId);

        if (index < 0)
        {
            Debug.LogWarning($"[경영 복원] 업그레이드 건물을 찾을 수 없습니다: {buildingId}",this);

            return false;
        }

        if (level < 0)
        {
            Debug.LogError($"[경영 복원] 업그레이드 건물 레벨이 음수입니다: {buildingId}={level}",this);

            return false;
        }

        IReadOnlyList<BuildingAsset.UpgradeStep> levels = _upgradeLevelTables[index];

        int maxLevel = levels != null ? levels.Count : 0;

        if (level > maxLevel)
        {
            Debug.LogError($"[경영 복원] 업그레이드 건물 레벨이 현재 최대치를 초과합니다: {buildingId}={level}, 최대={maxLevel}",this);

            return false;
        }

        return true;
    }

    /// <summary>
    /// BuildingID를 기준으로 업그레이드 건물 레벨을 복원한다.
    /// 비용 차감과 본진 레벨 제한을 적용하지 않는다.
    /// </summary>
    /// <param name="buildingId">복원할 업그레이드 건물 ID.</param>
    /// <param name="level">복원할 레벨.</param>
    /// <returns>검증과 적용에 성공하면 true.</returns>
    public bool TryRestoreUpgradeBuilding(string buildingId,int level)
    {
        if (!CanRestoreUpgradeBuilding(buildingId,level))
        {
            return false;
        }

        int index = FindUpgradeIndexById(buildingId);

        _upgradeLevel[index] = level;

        OnChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// 본진에서 증축한 주민 수를 절대값으로 복원한다.
    /// 비용 차감이나 증축 횟수 진행을 다시 수행하지 않는다.
    /// </summary>
    /// <param name="bonusVillagers">
    /// 복원할 증축 주민 수. 0 이상이어야 한다.
    /// </param>
    /// <returns>값이 유효하고 적용됐으면 true.</returns>
    public bool TryRestoreBonusVillagers(int bonusVillagers)
    {
        if (bonusVillagers < 0)
        {
            Debug.LogError($"[경영 복원] 증축 주민 수가 음수입니다: {bonusVillagers}",this);

            return false;
        }

        _bonusVillagers = bonusVillagers;

        OnChanged?.Invoke();

        return true;
    }

    // ── 되돌리기 등록 (#444) ─────────────────────────────────────────────
    /// <summary>
    /// 자원을 소모한 경영 조작을 되돌리기 히스토리에 올린다 — 타워 배치·합성과 <b>같은 스택</b>이라
    /// 건물 업그레이드 → 타워 배치 → 건물 업그레이드를 눌린 역순으로 하나씩 되감는다(LIFO, #444).
    /// </summary>
    ///
    /// 되돌리는 방법으로 위의 세이브 복원 API(<c>TryRestore*</c>)를 그대로 넘긴다 — "비용·페이즈 게이트를
    /// 타지 않고 상태를 그 값으로 맞춘다"가 되돌리기에 필요한 것과 정확히 같아서, 되돌리기 전용 감소
    /// 경로를 따로 만들지 않는다(근거는 <see cref="ResourceSpendCommand"/> 주석).
    ///
    /// 등록에 실패해도 조작 자체는 정상이므로 경고만 남기고 진행한다 — "되돌릴 수 없다" 하나만 잃는다
    /// (<c>TowerPlacer</c>가 배치 커맨드 인수 실패를 다루는 것과 같은 판단).
    private void PushSpendUndo(IReadOnlyList<ResourceCost> paid, Func<bool> revert, string label)
    {
        var command = new ResourceSpendCommand(this, paid, revert, label);
        if (command.Execute())
        {
            CommandHistory.Push(command); // Confirm()도 여기서 걸린다(등록과 확정은 한 몸)
            return;
        }

        Debug.LogWarning($"[경영] {label}: 되돌리기 커맨드 인수에 실패했습니다 — " +
                         "이 조작은 되돌릴 수 없습니다(조작 자체는 정상).", this);
    }

    /// <summary>
    /// 주민 증축 1회를 되돌린다(#444) — 상한을 <paramref name="previousBonus"/> 기준으로 되맞춘다.
    /// </summary>
    ///
    /// **배치가 상한을 넘게 되면 먼저 한 명을 뺀다.** 늘린 주민은 그날 바로 배치할 수 있고 그 배치는
    /// 히스토리를 거치지 않으므로(<see cref="AssignVillager"/>는 자원을 쓰지 않아 되돌리기 축의 바깥이다),
    /// 상한만 내리면 **없는 주민이 계속 생산하는 상태**가 남는다. 그래서 되돌리기가 배치까지 본다.
    ///
    /// 어느 라인에서 뺄지는 **가장 많이 배치된 라인**으로 정한다 — 임의 선택이지만 결정적이고, 분산
    /// 배치한 판을 가장 덜 흐트러뜨린다. 빼는 일 자체는 <see cref="UnassignVillager"/>를 그대로 쓰므로
    /// <see cref="BuildingAction.VillagerUnassigned"/>가 발화해 **그 건물에서 주민 1명이 걸어 나온다** —
    /// 되돌렸다는 사실이 화면에서 읽히는 경로가 이미 있어서 연출을 새로 만들지 않는다(Resident.md §3.2).
    ///
    /// 뺄 수 없으면(있을 수 없지만) **상한을 내리지 않고 실패로 끝낸다** — 그러면 비용도 환원되지 않아
    /// (<see cref="ResourceSpendCommand"/>) 상태와 지갑이 함께 제자리에 남는다.
    /// <summary>
    /// 업그레이드 전용 건물의 되돌리기 — 복원 전에 <b>대상이 등록 시점의 그 건물인지 대조한다</b>(#444).
    /// </summary>
    ///
    /// 복원 API는 대상을 `BuildingID`로 **다시 찾는다**(세이브는 index가 없어 그럴 수밖에 없다). 그래서
    /// 같은 SO가 배열에 두 번 등록되면 `FindUpgradeIndexById`가 첫 일치를 잡아 **엉뚱한 건물이 되돌아간다**
    /// — 자원은 환원되는데 레벨은 남의 것이 내려가고 콘솔은 조용하다. 되돌리기는 등록 시점의 index를
    /// 쥐고 있으므로 여기서 대조해 그 경로를 막는다(SO 유일성 전제 WL-021을 상속하지 않는다).
    ///
    /// ⚠ 실패는 **에러로 남긴다**. `CommandHistory.Undo`가 커맨드를 실행 전에 스택에서 빼므로 실패한
    /// 항목은 재시도되지 않는다 — 조용히 넘어가면 "눌렀는데 아무 일도 없다"로만 보인다(WL-148).
    private bool TryRevertUpgradeBuilding(string buildingId, int registeredIndex, int previousLevel)
    {
        if (FindUpgradeIndexById(buildingId) != registeredIndex)
        {
            Debug.LogError($"[되돌리기] {buildingId}: 되돌릴 대상을 다시 찾지 못했습니다(등록 index {registeredIndex}) — " +
                           "같은 건물 SO가 _upgradeBuildings에 중복 등록됐는지 확인하세요.", this);
            return false;
        }

        return TryRestoreUpgradeBuilding(buildingId, previousLevel);
    }

    /// 생산 라인의 되돌리기 — 대조 근거는 <see cref="TryRevertUpgradeBuilding"/>과 같다.
    /// 주민 배치 수는 스냅샷하지 않고 **되돌리는 시점의 값**을 읽어 그대로 유지한다.
    private bool TryRevertProductionLine(string buildingId, int registeredIndex, int previousLevel)
    {
        if (FindLineIndexById(buildingId) != registeredIndex)
        {
            Debug.LogError($"[되돌리기] {buildingId}: 되돌릴 대상을 다시 찾지 못했습니다(등록 index {registeredIndex}) — " +
                           "같은 건물 SO가 _productionBuildings에 중복 등록됐는지 확인하세요.", this);
            return false;
        }

        return TryRestoreProductionLine(buildingId, previousLevel, LineVillagers(registeredIndex));
    }

    private bool RevertVillagerGrowth(int previousBonus)
    {
        int restoredMax = _maxVillagers + previousBonus;

        while (AssignedTotal > restoredMax)
        {
            int line = FullestLineIndex();
            if (line < 0 || !UnassignVillager(line))
            {
                Debug.LogWarning($"[되돌리기] 주민 증축: 배치 {AssignedTotal}명을 상한 {restoredMax}명으로 " +
                                 "줄일 수 없어 되돌리지 않았습니다.", this);
                return false;
            }
        }

        return TryRestoreBonusVillagers(previousBonus);
    }

    // 주민이 가장 많이 배치된 라인(전부 0이면 -1). 동수면 낮은 index가 이긴다 — 같은 판에서 되돌리기가
    // 두 번 불려도 같은 선택을 하도록 결정적으로 둔다.
    private int FullestLineIndex()
    {
        if (_villagerCounts == null) return -1;

        int best = -1;
        for (int i = 0; i < _villagerCounts.Length; i++)
        {
            if (_villagerCounts[i] > 0 && (best < 0 || _villagerCounts[i] > _villagerCounts[best])) best = i;
        }
        return best;
    }
}
