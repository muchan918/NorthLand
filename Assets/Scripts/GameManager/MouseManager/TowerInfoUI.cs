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
    [Tooltip("스탯 블록 전체. 스탯 문자열이 비면(오라 전용 타워 등) 통째로 숨긴다.")]
    [SerializeField] GameObject _statsContainer;
    [SerializeField] TextMeshProUGUI _statsText;

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
    [Tooltip("후보 칸 프리팹(TowerButton 복제 + TowerMergeTargetSlot). 비면 블록이 뜨지 않는다.")]
    [SerializeField] TowerMergeTargetSlot _mergeSlotPrefab;

    // 이번 표시로 만든 칸들. **`_mergeContent.childCount`로 대신하면 안 된다** — `Destroy`는 프레임 끝에
    // 반영되므로, 같은 프레임에 비우고 다시 채우는 이 경로에서는 childCount가 방금 지운 칸까지 세어
    // "표시할 게 0인데 블록이 켜진" 상태가 된다.
    private readonly List<TowerMergeTargetSlot> _mergeSlots = new();

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
    /// 타워 메타데이터로 패널을 채운다(정본 경로).<br/>
    /// <paramref name="statsText"/>는 이미 조합된 평문(공격력/사거리 등 SO 수치) — 숫자값이라 로컬라이즈 대상이 아니다.
    /// </summary>
    // 로컬라이즈는 지속형 패널에 LocalizationHelper.Get을 쓰는 방식이라 로케일 변경 자동 갱신이 안 되는 한계가 있다
    // (BuildingInfoUI와 동일한 트레이드오프, #153 — 필요 시 후속으로 LocalizeStringEvent로 함께 교체).
    /// <paramref name="targeting"/>가 null이면 조준 전환 행을 숨긴다 — 조준 개념이 없는 타워가 있고(오라 전용),
    /// 안 먹는 조작을 띄우는 것은 조작이 아예 없는 것보다 나쁘기 때문이다.
    public void ShowInfo(TowerData data, string statsText = null, ITargetingSelector targeting = null)
    {
        if (data == null)
        {
            HideInfo();
            return;
        }

        SetText(_nameText, L(data.NameKey));
        SetText(_roleText, L(data.RoleKey));
        SetText(_descriptionText, L(data.DescriptionKey));
        ApplyStats(statsText);
        ApplyTargeting(targeting);
        RefreshMergeTargets(data.TowerID);
        gameObject.SetActive(true);
    }

    /// <summary>설명만 아는 호출부(테스트 헬퍼 등)를 위한 축약 경로. 이름·역할은 비워 둔다.</summary>
    public void ShowInfo(string descriptionKey, string statsText = null)
    {
        SetText(_nameText, string.Empty);
        SetText(_roleText, string.Empty);
        SetText(_descriptionText, L(descriptionKey));
        ApplyStats(statsText);
        ApplyTargeting(null);   // 축약 경로는 타워 인스턴스를 모르므로 조준 조작을 붙일 수 없다
        RefreshMergeTargets(null);  // TowerID를 모르면 역방향 조회를 할 수 없다 → 블록을 접는다
        gameObject.SetActive(true);
    }

    public void HideInfo()
    {
        SetText(_nameText, string.Empty);
        SetText(_roleText, string.Empty);
        SetText(_descriptionText, string.Empty);
        SetText(_statsText, string.Empty);
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

    // 스탯이 없는 타워(오라 전용 등 DescribeStats가 빈 경우)는 블록을 통째로 숨겨 빈 박스가 남지 않게 한다.
    private void ApplyStats(string statsText)
    {
        SetText(_statsText, statsText);
        if (_statsContainer != null)
        {
            _statsContainer.SetActive(!string.IsNullOrEmpty(statsText));
        }
    }

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

        if (_mergeContent != null && _mergeSlotPrefab != null && !string.IsNullOrEmpty(materialTowerId))
        {
            // 표시 순서는 뷰가 정한다(색인은 카탈로그 적재 순서 그대로 — Resources.LoadAll이라 비결정적).
            // 등급 다음 표시 이름: 도감(FusionTowerCodexUI.LoadData)과 같은 규칙이라 두 화면의 순서가 일치한다.
            var sorted = new List<TowerRecipe>(TowerMergeTargetIndex.RecipesUsing(materialTowerId));
            sorted.Sort(CompareByRarityThenName);

            foreach (TowerRecipe recipe in sorted)
            {
                TowerAsset result = recipe.Result; // 색인이 Result 없는 레시피를 이미 걸렀다

                TowerMergeTargetSlot slot = Instantiate(_mergeSlotPrefab, _mergeContent);
                slot.Set(result.Icon, TowerDisplayName.Of(result));

                // 상위 타워의 스탯·코스트는 호버 툴팁이 맡는다 — 기존 감지기를 런타임 부착해 재사용하므로
                // 칸 프리팹에 툴팁 배선이 필요 없다(TowerSelectPanelView·합성 패널과 같은 선례).
                // 프리팹에 이미 붙어 있으면 [DisallowMultipleComponent]가 AddComponent를 거부하므로 먼저 조회한다.
                if (!slot.TryGetComponent(out TowerTooltipSource tooltip))
                {
                    tooltip = slot.gameObject.AddComponent<TowerTooltipSource>();
                }
                tooltip.Init(result); // Data는 TowerDisplayName.Of가 이미 채웠다(툴팁이 키를 읽을 수 있다)

                _mergeSlots.Add(slot);
            }
        }

        if (_mergeContainer != null)
        {
            _mergeContainer.SetActive(_mergeSlots.Count > 0);
        }
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
