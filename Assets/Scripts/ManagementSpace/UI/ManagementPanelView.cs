using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 경영 패널 뷰. <see cref="ManagementController"/>를 구독해 자원 HUD·주민 풀·페이즈·생산/자원 라인을 렌더하고,
/// '밤으로' 버튼을 컨트롤러에 연결한다. 로직은 전혀 갖지 않는다(위젯 참조 + 렌더링만) —
/// 실제 UI 아트로 교체 시 이 뷰의 인스펙터 참조만 다시 연결하면 컨트롤러/모델은 그대로다.<br/>
/// <br/>
/// 생산/자원 라인 구성(#166, Resources.md §5.5): <b>고정 행</b>을 시작 시 한 번만 만든다(동적 등록 없음).<br/>
/// ① 기본 자원 생산 라인(나무·철·식량, +/- 배치) → ② 마나석(표시 전용) → ③ 특수 자원(금·루비·사파이어·다이아, 표시 전용).<br/>
/// 특수 자원은 모두 항상 보이되 <b>확보(수급량&gt;0) 전엔 회색</b>이며, 매 갱신마다 <b>활성 행이 위로</b> 재정렬된다.<br/>
/// (Docs/ManagementArea/Resources.md — 이슈 #43·#166)
/// </summary>
public class ManagementPanelView : MonoBehaviour
{
    private const string k_DayStringTableKey = "game.system.day";
    private const string k_NightStringTableKey = "game.system.night";
    private const string k_WaveStringTableKey = "game.system.wave";
    private const string k_DefenseStringTableKey = "game.system.defense";

    [Header("컨트롤러")]
    [SerializeField] ManagementController _controller;

    // 자원 지갑(보유량) 총량 표기는 탑 바에서 생산 라인 각 행의 지갑 칸으로 이관됨(#166, Resources.md §5.5 (C)).
    // 여기서 더 이상 관리하지 않는다 — ProductionLineView가 행별로 ResourceCount를 표시한다.

    [Header("주민 풀 / 페이즈")]
    [SerializeField] TMP_Text _villagerPoolText;
    [SerializeField] TMP_Text _phaseText;
    [SerializeField] Button _endDayButton;

    [Header("생산/자원 라인")]
    [SerializeField] Transform _lineContainer;
    [SerializeField] ProductionLineView _linePrefab;

    private readonly List<ProductionLineView> _villagerViews = new(); // 기본 자원 라인(+/-)
    private readonly List<ProductionLineView> _specialViews = new();  // 특수 자원 표시 행(회색/재정렬)
    private ProductionLineView _manaView;                             // 마나석 표시 행
    private readonly List<ProductionLineView> _ordered = new();       // 재정렬용 재사용 버퍼

    private void Start()
    {
        if (_controller == null)
        {
            _controller = FindFirstObjectByType<ManagementController>();
        }
        if (_controller == null)
        {
            Debug.LogError("[경영패널] ManagementController를 찾을 수 없습니다.");
            return;
        }

        BuildLines();

        if (_endDayButton != null)
        {
            _endDayButton.onClick.RemoveAllListeners();
            _endDayButton.onClick.AddListener(_controller.RequestAdvancePhase);
        }

        _controller.OnChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (_controller != null)
        {
            _controller.OnChanged -= Refresh;
        }
    }

    // 고정 행을 한 번만 만든다(#166): 기본 자원 라인 → 마나석 → 특수 자원. 확보 시엔 재빌드 없이 Refresh만.
    private void BuildLines()
    {
        if (_lineContainer == null || _linePrefab == null)
        {
            Debug.LogError("[경영패널] lineContainer/linePrefab이 연결되지 않았습니다.");
            return;
        }

        // ① 기본 자원 생산 라인(주민 배치 +/-)
        for (int i = 0; i < _controller.LineCount; i++)
        {
            ProductionLineView view = Instantiate(_linePrefab, _lineContainer);
            view.BindVillager(_controller, i);
            _villagerViews.Add(view);
        }

        // ② 마나석 표시 행(배치 없음, +n = 웨이브 클리어 마나 미리보기)
        _manaView = Instantiate(_linePrefab, _lineContainer);
        _manaView.BindResourceDisplay(_controller, ResourceKind.Mana, ResourceNameKey(ResourceKind.Mana), true);

        // ③ 특수 자원 표시 행(배치 없음, +n = 일일 수급량, 미개방 시 회색) — 모두 항상 생성(#166)
        foreach (ResourceKind kind in SpecialKinds())
        {
            ProductionLineView view = Instantiate(_linePrefab, _lineContainer);
            view.BindResourceDisplay(_controller, kind, ResourceNameKey(kind), false);
            _specialViews.Add(view);
        }
    }

    private void Refresh()
    {
        if (_villagerPoolText != null)
        {
            _villagerPoolText.text = $"{_controller.AssignedTotal}/{_controller.MaxVillagers}";
        }
        if (_phaseText != null)
        {
            _phaseText.text = _controller.IsDay ? $"{LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, k_WaveStringTableKey)} {_controller.CurrentWave}" : $"{LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, k_NightStringTableKey)} ({LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, k_DefenseStringTableKey)})";
        }
        if (_endDayButton != null)
        {
            _endDayButton.interactable = _controller.CanAdvancePhase;
        }

        RefreshLines();
    }

    // 모든 행을 갱신하고, 특수 자원 행을 활성(수급량>0) 우선으로 재정렬한다(#166).
    private void RefreshLines()
    {
        for (int i = 0; i < _villagerViews.Count; i++)
        {
            _villagerViews[i].Refresh();
        }
        if (_manaView != null)
        {
            _manaView.Refresh();
        }
        for (int i = 0; i < _specialViews.Count; i++)
        {
            _specialViews[i].Refresh();
        }

        // 최종 순서: 기본 라인 → 마나 → 활성 특수 → 미개방 특수. 순서대로 SiblingIndex를 재지정한다.
        _ordered.Clear();
        _ordered.AddRange(_villagerViews);
        if (_manaView != null)
        {
            _ordered.Add(_manaView);
        }
        for (int i = 0; i < _specialViews.Count; i++)
        {
            if (_specialViews[i].IsActive) _ordered.Add(_specialViews[i]);
        }
        for (int i = 0; i < _specialViews.Count; i++)
        {
            if (!_specialViews[i].IsActive) _ordered.Add(_specialViews[i]);
        }

        for (int i = 0; i < _ordered.Count; i++)
        {
            _ordered[i].transform.SetSiblingIndex(i);
        }
    }

    // 자원 표시명 스트링 테이블 키 — CSV(ResourceTable) NameKey와 동일한 규약 game.resources.{종류소문자}.
    private static string ResourceNameKey(ResourceKind kind) => $"game.resources.{kind.ToString().ToLowerInvariant()}";

    // 특수 자원(미개척 영지 해금) = 기본 3종·마나를 제외한 모든 ResourceKind(금/루비/사파이어/다이아, #166).
    private static IEnumerable<ResourceKind> SpecialKinds()
    {
        foreach (ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))
        {
            if (kind != ResourceKind.Wood && kind != ResourceKind.Iron && kind != ResourceKind.Food && kind != ResourceKind.Mana)
            {
                yield return kind;
            }
        }
    }
}
