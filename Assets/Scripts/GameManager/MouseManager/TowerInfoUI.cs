using System.Collections.Generic;
using NorthLand.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// 타워 정보 패널 UI. 나중에 UIManager가 관리할 것을 고려해 싱글톤으로 접근한다.
/// (주의) 이 오브젝트는 씬에서 '활성' 상태로 둬야 Awake가 실행되어 Instance가 등록된다.
///        숨김 처리는 Awake에서 하므로, 인스펙터에서 미리 꺼두지 말 것.
///
/// 표시 구성: 이름 · 역할 한 줄 · 설명 / 스탯 블록 / 합성 후보 블록.
/// BuildingInfoUI·StorePanelUI와 같은 pull 방식(호출부가 데이터를 주고 뷰는 그리기만 한다).
public class TowerInfoUI : MonoBehaviour
{
    public static TowerInfoUI Instance { get; private set; }

    [Header("제목 / 설명")]
    [SerializeField] TextMeshProUGUI _nameText;
    [Tooltip("역할 한 줄 요약 (TowerData.RoleKey)")]
    [SerializeField] TextMeshProUGUI _roleText;
    [SerializeField] TextMeshProUGUI _descriptionText;

    [Header("스탯")]
    [Tooltip("스탯 블록 전체. 표시할 행이 하나도 없으면 통째로 숨긴다.")]
    [SerializeField] GameObject _statsContainer;
    [Tooltip("스탯 행. 씬에 배치한 TowerStatRow 인스턴스를 순서대로 배선한다. " +
             "표시할 항목이 배열보다 적으면 남는 행은 숨기고, 많으면 1회 경고한다.")]
    [SerializeField] TowerStatRowView[] _statRows;

    [Header("조준 전환 (#387)")]
    [Tooltip("조준 전환 행 전체. 조준 개념이 없는 타워(오라 전용 등)에서는 통째로 숨긴다.")]
    [SerializeField] GameObject _targetingContainer;
    [Tooltip("현재 조준 방식 이름")]
    [SerializeField] TextMeshProUGUI _targetingText;
    [SerializeField] Button _targetingPrevButton;
    [SerializeField] Button _targetingNextButton;

    // 지금 패널이 조작하는 타워. **`Tower`가 아니라 좁은 계약으로 잡는다** — 뷰가 타워의 전체 표면을
    // 알기 시작하면 pull 방식이 무너진다(ITargetingSelector 주석).
    ITargetingSelector _targeting;

    [Header("합성 후보 — 이 타워를 재료로 쓰는 상위 타워 (TowerMerge.md §8.5)")]
    [Tooltip("합성 후보 블록 전체. 표시할 상위 타워가 하나라도 있을 때만 켠다.")]
    [SerializeField] GameObject _mergeContainer;
    [Tooltip("후보 칸이 생성될 부모 = 배치 팔레트에서 복사해 온 Scroll View의 Content.")]
    [SerializeField] Transform _mergeContent;
    [Tooltip("후보 칸 프리팹(TowerButton 복제 + TowerMergeTargetSlot, NorthLand-Imported 소속). 비면 블록이 뜨지 않고 경고를 1회 남긴다.")]
    [SerializeField] TowerMergeTargetSlot _mergeSlotPrefab;

    // 이번 표시로 만든 칸들. **`_mergeContent.childCount`로 대신하면 안 된다** — `Destroy`는 프레임 끝에
    // 반영되므로, 같은 프레임에 비우고 다시 채우는 이 경로에서는 childCount가 방금 지운 칸까지 세어
    // "표시할 게 0인데 블록이 켜진" 상태가 된다.
    private readonly List<TowerMergeTargetSlot> _mergeSlots = new();
    private bool _mergeWiringWarned; // 아래 HasMergeSlotWiring — 경고 1회 제한용

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 버튼 배선은 여기서 1회만 한다 — ShowInfo마다 걸면 선택할 때마다 리스너가 쌓여
        // 한 번 누를 때 여러 칸이 넘어간다. 배선이 비어 있어도(프리팹 미배선) 그냥 넘어간다.
        if (_targetingPrevButton != null) _targetingPrevButton.onClick.AddListener(() => CycleTargeting(-1));
        if (_targetingNextButton != null) _targetingNextButton.onClick.AddListener(() => CycleTargeting(1));

        HideInfo(); // Instance 등록 후 숨기므로 안전
    }

    // 조준 전환 버튼의 동작. 라벨만 다시 그리고 패널 전체는 건드리지 않는다 —
    // ShowInfo를 다시 부르면 스탯·합성 블록까지 재구성되어 스크롤·레이아웃이 튄다.
    void CycleTargeting(int step)
    {
        if (_targeting == null) return;

        _targeting.CycleTargeting(step);
        SetText(_targetingText, _targeting.TargetingName);
    }

    /// <summary>
    /// 타워 메타데이터로 패널을 채운다(정본 경로).
    /// </summary>
    // 로컬라이즈는 지속형 패널에 LocalizationHelper.Get을 쓰는 방식이라 로케일 변경 자동 갱신이 안 되는 한계가 있다
    // (BuildingInfoUI와 동일한 트레이드오프, #153 — 필요 시 후속으로 LocalizeStringEvent로 함께 교체).
    /// <paramref name="targeting"/>가 null이면 조준 전환 행을 숨긴다 — 조준 개념이 없는 타워가 있고(오라 전용),
    /// 안 먹는 조작을 띄우는 것은 조작이 아예 없는 것보다 나쁘기 때문이다.
    /// <paramref name="statSource"/>가 null이면 스탯 블록을 접는다 — 배치 전 툴팁처럼 원장이 없는
    /// 호출부는 기본값과 실제값을 가를 수 없어 행을 만들 수 없다.
    public void ShowInfo(TowerData data, ITargetingSelector targeting = null,
                         ITowerStatRowSource statSource = null)
    {
        if (data == null)
        {
            HideInfo();
            return;
        }

        SetText(_nameText, L(data.NameKey));
        SetText(_roleText, L(data.RoleKey));
        SetText(_descriptionText, L(data.DescriptionKey));
        ApplyStats(statSource);
        ApplyTargeting(targeting);
        RefreshMergeTargets(data.TowerID);
        gameObject.SetActive(true);
    }

    /// <summary>설명만 아는 호출부(테스트 헬퍼 등)를 위한 축약 경로. 이름·역할은 비워 둔다.
    /// 스탯 행은 액션과 원장에서 나오므로 이 경로에서는 만들 수 없다 — 블록을 접는다.</summary>
    public void ShowInfo(string descriptionKey)
    {
        SetText(_nameText, string.Empty);
        SetText(_roleText, string.Empty);
        SetText(_descriptionText, L(descriptionKey));
        ApplyStats(null);
        ApplyTargeting(null);   // 축약 경로는 타워 인스턴스를 모르므로 조준 조작을 붙일 수 없다
        RefreshMergeTargets(null);  // TowerID를 모르면 역방향 조회를 할 수 없다 → 블록을 접는다
        gameObject.SetActive(true);
    }

    public void HideInfo()
    {
        SetText(_nameText, string.Empty);
        SetText(_roleText, string.Empty);
        SetText(_descriptionText, string.Empty);
        ApplyStats(null);       // 스탯 행을 접는다 — 직전 타워의 행이 남지 않게(#536)
        ApplyTargeting(null);   // 선택이 풀린 타워를 계속 붙들지 않는다(파괴된 타워를 조작하는 경로 차단)
        RefreshMergeTargets(null);  // 다음 표시가 축약 경로여도 직전 타워의 후보가 남지 않게 비운다
        gameObject.SetActive(false);
    }

    // 조준 전환 행을 대상 타워에 맞춘다. 대상이 없으면 행을 통째로 숨기고 참조도 놓는다.
    private void ApplyTargeting(ITargetingSelector targeting)
    {
        _targeting = targeting;

        if (_targetingContainer != null)
        {
            _targetingContainer.SetActive(targeting != null);
        }

        SetText(_targetingText, targeting?.TargetingName);
    }

    // 지금 행을 공급하는 타워. 파괴·선택 해제 시 놓는다(ITargetingSelector와 같은 규약).
    private ITowerStatRowSource _statSource;
    // 마지막으로 그린 원장 버전. Update가 이 값과 비교해 **바뀐 프레임에만** 행을 다시 만든다.
    private int _statVersion;
    // 행 조립 버퍼. 패널이 소유한다 — 예전엔 Tower가 static 스크래치를 넘겨줬는데, 이제 패널이
    // 매 갱신마다 직접 채우므로 소유자가 여기인 편이 수명이 명확하다.
    private readonly List<TowerStatRowData> _rowBuffer = new();

    // 낼 행이 하나도 없는 타워는 블록을 통째로 숨겨 빈 박스가 남지 않게 한다.
    private void ApplyStats(ITowerStatRowSource statSource)
    {
        _statSource = statSource;
        _statVersion = statSource?.StatsVersion ?? 0;

        RebuildStatRows();
    }

    /// <summary>
    /// 원장이 바뀌었으면 행을 다시 그린다(#536). 램프 스택이 쌓이는 동안 패널을 다시 열지 않아도
    /// 공격력·공격속도가 따라 움직이게 하는 경로다 — 타일 버프·오라·스킬 버프·버프 만료도 같은 축이라
    /// 함께 반영된다(전부 원장을 거쳐 값이 되기 때문).
    /// <para>매 프레임 행을 재조립하지 않는 이유는 문자열 조립 비용이다. 실제로 값이 바뀌는 순간은
    /// 드물어서 <see cref="ITowerStatRowSource.StatsVersion"/> 비교만 매 프레임 한다.</para>
    /// </summary>
    private void Update()
    {
        if (_statSource == null) return;

        // 합성으로 소모된 타워처럼 **파괴된** 공급원은 `== null`로 걸러지지 않는다(인터페이스 참조라
        // C# null 검사가 Unity의 파괴 판정을 타지 않는다). UnityEngine.Object로 되짚어 검사한다 —
        // OnDeselected가 오지 않는 경로(타워가 합성 재료로 사라짐)에서 여기가 마지막 방어선이다.
        if (_statSource is UnityEngine.Object unityObject && unityObject == null)
        {
            HideInfo();
            return;
        }

        int version = _statSource.StatsVersion;
        if (version == _statVersion) return;

        _statVersion = version;
        RebuildStatRows();
    }

    private void RebuildStatRows()
    {
        _rowBuffer.Clear();
        _statSource?.BuildStatRows(_rowBuffer);

        int filled = ApplyStatRows(_rowBuffer);

        if (_statsContainer != null)
        {
            _statsContainer.SetActive(filled > 0);
        }
    }

    /// <summary>
    /// 스탯 행을 채우고 남는 행은 숨긴다. 채운 행 수를 돌려준다.
    /// <para>행이 배열보다 많으면 넘치는 항목은 **표시되지 않는다** — 조용히 잘리지 않도록 1회 경고한다.
    /// 행 수는 타워마다 다르다(공격 3축 + 연발·구역·효과·성장…) — 효과가 많은 타워가 배선된 행 수를
    /// 넘길 수 있으므로, 잘린 사실이 콘솔에 남아야 "왜 저 타워만 독 표기가 없지"로 헤매지 않는다.</para>
    /// </summary>
    private int ApplyStatRows(IReadOnlyList<TowerStatRowData> statRows)
    {
        if (_statRows == null) return 0;

        int count = statRows?.Count ?? 0;

        if (count > _statRows.Length && !_statRowOverflowWarned)
        {
            _statRowOverflowWarned = true;
            Debug.LogWarning($"[타워정보] 스탯 행이 {count}개인데 배선된 행은 {_statRows.Length}개입니다 — " +
                             $"{count - _statRows.Length}개가 표시되지 않습니다. 씬에 TowerStatRow를 더 배치하세요.", this);
        }

        int filled = 0;

        for (int i = 0; i < _statRows.Length; i++)
        {
            if (_statRows[i] == null) continue;

            if (i < count)
            {
                _statRows[i].Set(statRows[i]);
                filled++;
            }
            else
            {
                _statRows[i].Hide();
            }
        }

        return filled;
    }

    // 행 부족 경고는 배선이 바뀌기 전까지 사실이 변하지 않으므로 1회만 낸다.
    private bool _statRowOverflowWarned;

    /// <summary>
    /// "이 타워를 재료로 쓰는 상위 타워" 블록을 다시 그린다(TowerMerge.md §8.5).
    /// <paramref name="materialTowerId"/>가 없거나 그 타워가 어떤 레시피의 재료도 아니면 블록을 접는다.
    /// </summary>
    // 조회는 TowerMergeTargetIndex(= TowerFusionMatcher.BuildRequired 기반) 한 곳만 쓴다 —
    // 재료 판정을 여기서 다시 구현하면 표시와 실제 합성 가능성이 갈린다(TowerMerge.md §6 단일 출처).
    //
    // 밤에도 이 블록은 뜬다. 코디네이터의 낮 게이팅은 **실행**에 걸리는 것이고(§10), 이건 조작이 아니라
    // 정보다 — 밤에 "이 타워로 무엇을 만들 수 있었나"를 감추면 다음 낮 계획을 세울 수 없다.
    private void RefreshMergeTargets(string materialTowerId)
    {
        ClearMergeSlots();

        if (!string.IsNullOrEmpty(materialTowerId) && HasMergeSlotWiring())
        {
            // 표시 순서는 뷰가 정한다(색인은 카탈로그 적재 순서 그대로 — Resources.LoadAll이라 비결정적).
            // 등급 다음 표시 이름: 도감(FusionTowerCodexUI.LoadData)과 같은 규칙이라 두 화면의 순서가 일치한다.
            var sorted = new List<TowerRecipe>(TowerMergeTargetIndex.RecipesUsing(materialTowerId));
            sorted.Sort(CompareByRarityThenName);

            foreach (TowerRecipe recipe in sorted)
            {
                TowerAsset result = recipe.Result; // 색인이 Result 없는 레시피를 이미 걸렀다

                TowerMergeTargetSlot slot = Instantiate(_mergeSlotPrefab, _mergeContent);

                // 정본 프리팹은 **아이콘만** 그린다(이름 칸이 없다 — 의도, TowerMerge.md §8.5).
                // 그래도 이름을 계속 넘기는 이유가 둘 있다: ① 이름 칸을 가진 변종 프리팹이 쓸 값이고,
                // ② 이 호출이 `EnsureData`로 `result.Data`(런타임 전용, 에셋 미직렬화)를 채워 **아래 툴팁이
                // 이름·역할·설명 키를 읽게** 한다. "안 보이는 인자"라고 지우면 툴팁이 TowerID로 떨어진다.
                slot.Set(result.Icon, TowerDisplayName.Of(result));

                // 상위 타워의 스탯·코스트는 호버 툴팁이 맡는다 — 기존 감지기를 런타임 부착해 재사용하므로
                // 칸 프리팹에 툴팁 배선이 필요 없다(TowerSelectPanelView·합성 패널과 같은 선례).
                // 프리팹에 이미 붙어 있으면 [DisallowMultipleComponent]가 AddComponent를 거부하므로 먼저 조회한다.
                if (!slot.TryGetComponent(out TowerTooltipSource tooltip))
                {
                    tooltip = slot.gameObject.AddComponent<TowerTooltipSource>();
                }
                // 레시피까지 넘긴다 — 여기 뜨는 타워는 **합성으로만** 얻으므로 자원 코스트가 비어 있고,
                // 툴팁이 대신 낼 수 있는 유일한 코스트가 "무슨 타워 몇 개"다. 이 블록의 존재 이유가
                // "이걸로 무엇을 만들 수 있나"인데 무엇이 더 필요한지를 안 보여주면 반쪽이다(#445).
                // Data는 TowerDisplayName.Of가 이미 채웠다(툴팁이 키를 읽을 수 있다).
                tooltip.Init(result, recipe);

                _mergeSlots.Add(slot);
            }
        }

        if (_mergeContainer != null)
        {
            _mergeContainer.SetActive(_mergeSlots.Count > 0);
        }
    }

    /// <summary>
    /// 후보 칸을 만들 배선이 온전한가. 비어 있으면 **한 번만** 경고한다.
    /// </summary>
    // 이 경로는 타워를 누를 때마다 돈다 — 매번 짖으면 콘솔이 못 쓰게 되므로 1회로 제한한다.
    // 그래도 완전히 조용하면 안 되는 이유: 칸 프리팹이 **별도 저장소(NorthLand-Imported) 소속**이라
    // 미동기 환경에서는 참조가 풀려 null이 되고, 증상이 "타워를 눌러도 상위 타워가 안 뜬다" 하나로
    // 배선 누락과 구별되지 않는다(컴파일도 콘솔도 조용 — WL-040 계통, SystemMap §4 동기화 계약).
    private bool HasMergeSlotWiring()
    {
        if (_mergeContent != null && _mergeSlotPrefab != null) return true;

        if (!_mergeWiringWarned)
        {
            _mergeWiringWarned = true;
            Debug.LogWarning(
                "[TowerInfoUI] 합성 후보 칸 배선이 비어 '합성 가능' 블록을 띄울 수 없습니다 — " +
                $"_mergeContent={(_mergeContent != null ? "OK" : "null")}, " +
                $"_mergeSlotPrefab={(_mergeSlotPrefab != null ? "OK" : "null")}. " +
                "칸 프리팹은 NorthLand-Imported 소속입니다(@NorthLand/Prefabs/UI/TowerTargetSlot.prefab) — " +
                "인스펙터 배선과 Imported 저장소 동기화를 함께 확인하세요.", this);
        }
        return false;
    }

    private void ClearMergeSlots()
    {
        foreach (TowerMergeTargetSlot slot in _mergeSlots)
        {
            if (slot != null) Destroy(slot.gameObject);
        }
        _mergeSlots.Clear();
    }

    // 등급 오름차순 → 표시 이름. CompareOrdinal은 한글 음절이 유니코드상 가나다 순이라 그대로 쓸 수 있고,
    // 도감이 이미 같은 비교를 쓴다(두 화면의 정렬 규칙을 하나로 유지).
    private static int CompareByRarityThenName(TowerRecipe left, TowerRecipe right)
    {
        int rarity = left.Result.Rarity.CompareTo(right.Result.Rarity);
        if (rarity != 0) return rarity;

        return string.CompareOrdinal(TowerDisplayName.Of(left.Result), TowerDisplayName.Of(right.Result));
    }

    private static void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null)
        {
            label.text = value ?? string.Empty;
        }
    }

    private static string L(string key) =>
        string.IsNullOrEmpty(key) ? string.Empty : LocalizationHelper.Get(LocalizationHelper.k_TowersTable, key);
}
