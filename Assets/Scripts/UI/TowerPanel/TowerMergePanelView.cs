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
    [SerializeField] GameObject _selectedRowPrefab; // TowerMergeSelectedRowView(아이콘 + 이름) 포함 행

    [Header("후보 버튼 (하단 Horizontal Scroll)")]
    [SerializeField] Transform _candidateContent;
    [SerializeField] Button _candidateButtonPrefab;

    // 뷰를 함께 캐시한다 — 회색 처리와 interactable은 TowerButtonView가 함께 소유하므로(#470)
    // 갱신마다 GetComponent를 다시 하지 않고 생성 시점에 한 번만 잡는다.
    private readonly List<(Button button, TowerButtonView view, TowerRecipe recipe)> _candidates = new();
    private readonly List<GameObject> _rows = new();
    private bool _built;

    // 프리팹 배선 유실 경고는 세션당 1회 — 행은 선택이 바뀔 때마다 전부 다시 생성되므로
    // 인스턴스 플래그로는 갱신마다 같은 경고가 쏟아진다(TowerButtonView.s_bannerWiringWarned와 같은 규약).
    private static bool s_rowViewWarned;

    private void Awake() => BuildCandidates();

    private void OnEnable()
    {
        if (_coordinator != null)
        {
            _coordinator.OnGroupChanged += Refresh;

            // 자원이 바뀌면 후보 버튼의 활성 여부가 달라진다(WL-209). 선택 리스트는 그대로이므로
            // 행 재생성 없이 후보만 다시 칠한다 — 되돌리기 환불처럼 패널이 열린 채 자원이 변할 수 있다.
            _coordinator.OnAffordabilityChanged += RefreshCandidates;
        }
        Refresh(); // 활성화 시점의 현재 선택 상태로 동기화(코디네이터가 켠 직후)
    }

    private void OnDisable()
    {
        if (_coordinator != null)
        {
            _coordinator.OnGroupChanged -= Refresh;
            _coordinator.OnAffordabilityChanged -= RefreshCandidates;
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

            // 아이콘만 채운다 — 이름 배너는 끈다(배치 팔레트와 같은 판단, #470). 이름·재료·계승 효과는
            // 바로 아래에서 붙이는 TowerMergeCandidateHover의 호버 툴팁이 이미 낸다(#213 §5.3) — 라벨을
            // 남겨두면 툴팁과 완전히 중복된다. `SetLocked`는 부르지 않는다 — 합성 후보에는 해금 개념이
            // 없고, 프리팹의 TowerLockOverlay는 `m_IsActive: 0`이라 그대로 조용하다(TowerMerge.md §8.5의 같은 판단).
            var view = button.GetComponent<TowerButtonView>();
            if (view != null) view.Set(recipe.Result != null ? recipe.Result.Icon : null);

            var captured = recipe; // 클로저 캡처(루프 변수 캡처 함정 회피)
            button.onClick.AddListener(() => { if (_coordinator != null) _coordinator.RequestMerge(captured); });

            // 호버 시 소모될 재료 타워만 핑크 아웃라인 + 결과 타워 툴팁(#213 §5.3). 버튼 프리팹을 편집하지
            // 않고 런타임 부착한다.
            button.gameObject.AddComponent<TowerMergeCandidateHover>().Init(_coordinator, captured);

            // 합성 결과는 설치음(성공) 또는 거절음(재료·코스트 부족)으로 스스로 답한다 —
            // 공용 클릭음까지 나면 두 소리가 겹쳐 들린다. 위와 같은 런타임 부착 방식으로 뺀다.
            UiClickSfxIgnore.ApplyTo(button);

            button.gameObject.SetActive(false); // 기본 숨김 — 매칭 시 켠다
            _candidates.Add((button, view, recipe));
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
            string label = TowerDisplayName.Of(tower.Asset);

            // 아이콘 소스는 아래 후보 버튼과 같은 `TowerAsset.Icon`이다(#535) — 표기 소스를 SO 한 곳에 둔다.
            var view = row.GetComponent<TowerMergeSelectedRowView>();
            if (view != null)
            {
                view.Set(tower.Asset != null ? tower.Asset.Icon : null, label);
            }
            else
            {
                // 뷰 없는 프리팹 변종 폴백 — 이름만 채운다(후보 버튼의 TowerButtonView 폴백과 같은 판단).
                // 프리팹이 별 저장소라 미동기 환경에서 이 경로를 탄다. 아이콘 없이 종전대로는 보인다.
                if (!s_rowViewWarned)
                {
                    s_rowViewWarned = true;
                    Debug.LogWarning("[타워합성] SelectedRow 프리팹에 TowerMergeSelectedRowView가 없습니다 — 아이콘 없이 이름만 표시합니다. " +
                                     "NorthLand-Imported의 SelectedRow.prefab 동기화를 확인하세요.", this);
                }

                var text = row.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = label;
            }

            _rows.Add(row);
        }
    }

    private void RefreshCandidates()
    {
        if (_coordinator == null) return;

        // 표시(재료)와 활성(코스트)을 **가른다**(WL-209).
        //   · 재료가 안 맞으면 아예 숨긴다 — 만들 수 없는 조합을 보여줄 이유가 없다.
        //   · 재료는 맞는데 자원이 모자라면 **회색으로 보여준다** — 예전에는 그냥 눌렸고 눌러도 조용히
        //     반려돼서, 자원을 더 모아야 하는지 타워를 더 놓아야 하는지 구분할 수 없었다.
        // 매칭 규칙은 TowerFusionMatcher 단일 출처, 코스트 판정은 실행부가 답한다(재구현 금지).
        foreach (var (button, view, recipe) in _candidates)
        {
            if (button == null) continue;

            bool matched = _coordinator.CanMerge(recipe);
            button.gameObject.SetActive(matched);

            // 숨긴 버튼의 표시는 의미가 없다 — 다시 켜질 때 이 자리에서 함께 정해진다.
            if (!matched) continue;

            // 회색 처리와 interactable을 **뷰가 함께 세운다**(#470). Button.interactable만 세우면
            // 색 전이가 targetGraphic(테두리)에만 걸려 아이콘은 밝게 남고, 자원이 모자란 후보가
            // 배치 팔레트와 다른 모습이 된다 — 같은 화면에서 "회색"이 두 뜻을 갖는다.
            // 해금 개념이 없어 SetLocked를 부르지 않으므로 SetSelectable의 연출 지연 경로도 타지 않는다.
            bool tutorialAllows = TutorialInputGate.AllowsForDisplay(TutorialAction.MergeTower);
            bool canAfford = _coordinator.CanAffordMerge(recipe);
            bool affordable = canAfford && tutorialAllows;
            // 소리는 「코스트 부족이 유일한 사유」일 때만 낸다(TowerButtonView.OnDisabledClick) —
            // 튜토리얼 제한에 대고 "자원을 모으라"고 안내하면 거짓말이 된다.
            if (view != null) view.SetSelectable(affordable, tutorialAllows && !canAfford);
            else button.interactable = affordable;   // 뷰 없는 프리팹 변종 폴백
        }
    }
}
