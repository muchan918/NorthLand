using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [테스트 전용] 합성 레시피 족보를 텍스트로 나열하는 디버그 패널(#304).
/// 레시피당 한 줄, `결과 - 재료 + 재료 + 재료` 형식(개수만큼 재료 이름을 반복한다).
/// 구분자를 ASCII `-`/`+`로 쓰는 이유는 <see cref="BuildRows"/> 위 주석 참고.
/// 레시피는 정적 SO라 런타임에 변하지 않으므로 Start에서 한 번만 목록을 만들고,
/// 버튼은 <see cref="_panelRoot"/>의 표시 토글만 한다. 읽기 전용 — 게임 상태를 건드리지 않는다.
///
/// ⚠ 이 컴포넌트가 붙은 오브젝트는 씬에서 **활성**이어야 한다(비활성이면 Start가 안 돌아 목록이 통째로 빈다).
/// 껐다 켜는 대상은 자식인 <see cref="_panelRoot"/>다 — StorePanelUI와 같은 함정.
/// 프로덕션 코드가 아니라 Assets/Scripts/Test 소속.
/// </summary>
public class TowerRecipeDebugPanel : MonoBehaviour
{
    const string k_RecipeFolder = "ScriptableObjects/TowerRecipes";

    [Tooltip("필수: 켜고 끌 자식 패널 루트. 이 컴포넌트가 붙은 오브젝트 자신이면 안 된다.")]
    [SerializeField] GameObject _panelRoot;

    [SerializeField] Button _toggleButton;
    [SerializeField] Transform _content;    // ScrollRect의 Content
    [SerializeField] GameObject _rowPrefab; // TMP_Text 포함 행 (SelectedRow.prefab 재사용 가능)

    // Awake가 아니라 Start인 이유: 행 텍스트가 LocalizationSettings(전역 서비스) 동기 조회에 의존한다.
    // Awake는 로컬라이제이션 초기화와 순서가 겹칠 수 있어 한 프레임 뒤가 안전하다.
    // DataTableManager는 static 생성자로 첫 접근 시 자가 초기화되므로 순서 문제가 없다.
    void Start()
    {
        if (_toggleButton != null) _toggleButton.onClick.AddListener(Toggle);
        else Debug.LogWarning("[레시피디버그] 토글 버튼이 연결되지 않았습니다 — 패널을 열 수단이 없습니다.");

        // 먼저 만들고 나중에 숨긴다 — 생성 시점엔 계층이 활성이라 레이아웃이 정상 계산된다.
        BuildRows();

        if (_panelRoot != null) _panelRoot.SetActive(false);
        else Debug.LogError("[레시피디버그] _panelRoot가 연결되지 않았습니다.");
    }

    void OnDestroy()
    {
        if (_toggleButton != null) _toggleButton.onClick.RemoveListener(Toggle);
    }

    public void Toggle()
    {
        if (_panelRoot == null) return;
        _panelRoot.SetActive(!_panelRoot.activeSelf);
    }

    // 레시피는 정적 SO라 한 번만 만든다. 재생성 경로가 없으므로 행 중복이 구조적으로 불가능하다.
    //
    // ⚠ 구분자는 반드시 ASCII만 쓴다. 프로젝트 기본 한글 폰트(Pretendard-Regular SDF)는 2484자 Static
    // 에셋이라 '×'(U+00D7)·'→'(U+2192)가 없고, 그 문자를 쓰면 TMP가 Dynamic 폴백 에셋
    // (PretendardJP SDF)에 글리프를 구워 넣어 공유 폰트 에셋이 더티가 된다 — 남의 작업 트리를 더럽히는
    // 사고 유형이라 unity-cli-guide §7이 금지한다. 한글 타워 이름 자체는 Pretendard-Regular에 다 있다.
    void BuildRows()
    {
        if (_content == null || _rowPrefab == null)
        {
            Debug.LogError("[레시피디버그] Content/행 프리팹이 연결되지 않았습니다.");
            return;
        }

        // 합성 패널의 _recipes는 private이고 프리팹/씬 배선이 갈라져 있어, SO 폴더를 정본으로 열거한다.
        var recipes = Resources.LoadAll<TowerRecipe>(k_RecipeFolder);
        if (recipes.Length == 0)
        {
            Debug.LogWarning($"[레시피디버그] '{k_RecipeFolder}'에서 레시피를 찾지 못했습니다.");
            return;
        }

        // LoadAll 순서는 보장되지 않는다 — 실행마다 목록이 흔들리지 않게 이름순 고정.
        Array.Sort(recipes, (a, b) => string.CompareOrdinal(a.name, b.name));

        var sb = new StringBuilder();
        foreach (var recipe in recipes)
        {
            if (recipe == null) continue;

            sb.Clear();

            sb.Append(recipe.Result != null ? Label(recipe.Result.TowerID) : "(결과 없음)");
            sb.Append(" - ");

            // 재료 집계는 TowerFusionMatcher가 단일 출처(중복 엔트리 합산·무효 엔트리 제거) — 재구현 금지.
            var required = TowerFusionMatcher.BuildRequired(recipe);
            if (required.Count == 0)
            {
                sb.Append("(재료 없음)");
            }
            else
            {
                // BuildRequired가 개수를 합산해 주지만, 여기서는 다시 풀어 이름을 개수만큼 반복한다.
                // "×2" 같은 곱셈 표기 대신 실제 배치할 타워를 그대로 나열하는 편이 읽기 쉽다.
                bool first = true;
                for (int i = 0; i < required.Count; i++)
                {
                    string label = Label(required[i].id);
                    for (int c = 0; c < required[i].count; c++)
                    {
                        if (!first) sb.Append(" + ");
                        sb.Append(label);
                        first = false;
                    }
                }
            }

            var row = Instantiate(_rowPrefab, _content);
            var text = row.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = sb.ToString();
        }
    }

    // TowerID → TowerData.NameKey → NorthLand_Towers 로컬라이즈, 실패 시 TowerID 폴백.
    // BuildRequired가 TowerAsset이 아니라 ID를 주므로 TowerAsset.Data(비직렬화 런타임 캐시) 채움이 불필요하다.
    // 디버그 용도라 ID를 괄호로 병기해 SO 역추적을 쉽게 한다.
    //
    // ⚠ TowerTable.Get은 미스 시 Debug.LogError를 낸다 — CSV에 없는 TowerID가 SO에 들어 있다는
    // 데이터 누락 신호이지 이 패널의 버그가 아니다.
    static string Label(string towerID)
    {
        // 빈 ID로 조회하면 TowerTable이 불필요한 LogError를 내므로 먼저 걸러낸다.
        if (string.IsNullOrEmpty(towerID)) return "?";

        var data = DataTableManager.Get<TowerTable>("TowerTable")?.Get(towerID);
        if (data == null || string.IsNullOrEmpty(data.NameKey)) return towerID;

        // 로케일이 아직 초기화되지 않았으면 null이 온다(EditMode에서 재현됨 — exec로 확인).
        // 그대로 보간하면 " (archer_tower)"처럼 앞이 빈 줄이 되므로 ID 단독 폴백으로 되돌린다.
        string localized = LocalizationHelper.Get(LocalizationHelper.k_TowersTable, data.NameKey);
        return string.IsNullOrEmpty(localized) ? towerID : $"{localized} ({towerID})";
    }
}