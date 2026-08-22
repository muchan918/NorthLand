using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NorthLand.Combat;

/// 합성 패널 뷰(#183). 2개 이상 선택 시 TowerMergeCoordinator가 이 패널 루트를 SetActive(true)로 켠다.
/// 상단: 선택된 재료 리스트(선택 순서), 하단: 레시피별 후보 버튼(매칭되는 것만 SetActive).
/// 코디네이터만 참조한다(파사드) — 실행부·매칭·선택 집합에 직접 의존하지 않는다. Docs/Core/TowerMerge.md §8.
///
/// 참고 골격은 TowerSelectPanelView(배치 팔레트)지만, 대상이 List&lt;TowerRecipe&gt; + 매칭 여부 + RequestMerge다.
public class TowerMergePanelView : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] TowerMergeCoordinator _coordinator;

    [Header("선택 리스트 (상단 Vertical Scroll)")]
    [SerializeField] Transform _selectedListContent;
    [SerializeField] GameObject _selectedRowPrefab; // TMP_Text 포함 행

    [Header("후보 버튼 (하단 Horizontal Scroll)")]
    [SerializeField] Transform _candidateContent;
    [SerializeField] Button _candidateButtonPrefab;

    private readonly List<(Button button, TowerRecipe recipe)> _candidates = new();
    private readonly List<GameObject> _rows = new();
    private bool _built;

    private void Awake() => BuildCandidates();

    private void OnEnable()
    {
        if (_coordinator != null)
        {
            _coordinator.OnGroupChanged += Refresh;
        }
        Refresh(); // 활성화 시점의 현재 선택 상태로 동기화(코디네이터가 켠 직후)
    }

    private void OnDisable()
    {
        if (_coordinator != null)
        {
            _coordinator.OnGroupChanged -= Refresh;
        }
    }

    // 레시피당 버튼 1개 미리 생성 + 기본 숨김(SetActive false). 한 번만.
    private void BuildCandidates()
    {
        if (_built) return;
        _built = true;

        if (_candidateContent == null || _candidateButtonPrefab == null)
        {
            Debug.LogError("[TowerMerge] 후보 버튼 content/prefab이 연결되지 않았습니다.");
            return;
        }

        foreach (var recipe in TowerRecipeCatalog.All)
        {
            if (recipe == null) continue;

            // 결과 타워의 런타임 Data(에셋에 저장 안 됨)를 채워 라벨·툴팁이 키를 읽을 수 있게 한다(채움 규약).
            TowerDisplayName.EnsureData(recipe.Result);

            var button = Instantiate(_candidateButtonPrefab, _candidateContent);

            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = recipe.Result != null ? TowerDisplayName.Of(recipe.Result) : recipe.name;

            var captured = recipe; // 클로저 캡처(루프 변수 캡처 함정 회피)
            button.onClick.AddListener(() => { if (_coordinator != null) _coordinator.RequestMerge(captured); });

            // 호버 시 소모될 재료 타워만 핑크 아웃라인(#213 §5.3). 버튼 프리팹을 편집하지 않고 런타임 부착한다.
            button.gameObject.AddComponent<TowerMergeCandidateHover>().Init(_coordinator, captured);

            button.gameObject.SetActive(false); // 기본 숨김 — 매칭 시 켠다
            _candidates.Add((button, recipe));
        }
    }

    private void Refresh()
    {
        RefreshSelectedList();
        RefreshCandidates();
    }

    private void RefreshSelectedList()
    {
        foreach (var row in _rows)
        {
            if (row != null) Destroy(row);
        }
        _rows.Clear();

        if (_coordinator == null || _selectedListContent == null || _selectedRowPrefab == null) return;

        foreach (var tower in _coordinator.SelectedTowers)
        {
            if (tower == null) continue;

            var row = Instantiate(_selectedRowPrefab, _selectedListContent);
            var text = row.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = TowerDisplayName.Of(tower.Asset);
            _rows.Add(row);
        }
    }

    private void RefreshCandidates()
    {
        if (_coordinator == null) return;
        // 매칭되는 레시피의 버튼만 켠다(포함 매칭 — TowerFusionMatcher 단일 출처, 재구현 금지).
        foreach (var (button, recipe) in _candidates)
        {
            if (button == null) continue;

            button.gameObject.SetActive(_coordinator.CanMerge(recipe));
        }
    }
}
