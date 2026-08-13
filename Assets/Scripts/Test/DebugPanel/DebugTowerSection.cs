using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [테스트 전용] 전 타워를 자원 소모 없이 배치하는 서브패널(#350).
/// 목록은 Resources 폴더의 <see cref="TowerAsset"/>을 전부 열거해 만든다 — 합성으로만 얻는 타워도
/// 포함되고, 새 타워가 추가돼도 씬 작업이 필요 없다.
///
/// 무료화는 <see cref="TowerPlacer.BeginTowerPlacement"/>에 cost: null을 넘기는 것이 전부다.
/// ManagementController의 CanAfford/TrySpend가 null 비용을 "무료"로 통과시키고,
/// TowerPlaceCommand에도 빈 지불 기록이 남아 되돌리기 환원이 0이 된다 — 프로덕션 코드는 무수정.
/// 배치 자체는 정상 고스트 플로우를 그대로 탄다(타일 검증·풋프린트·등장 연출 유지).
/// 즉 "돈만 안 내는" 배치이지 검증 우회가 아니다.
///
/// ⚠ 이 컴포넌트가 붙은 오브젝트는 씬에서 **활성**이어야 한다 — 비활성이면 Start가 안 돌아
/// 목록이 통째로 빈다. 껐다 켜는 대상은 참조로 받은 <see cref="_panelRoot"/>다.
/// 프로덕션 코드가 아니라 Assets/Scripts/Test 소속.
/// </summary>
public class DebugTowerSection : MonoBehaviour
{
    const string k_TowerFolder = "ScriptableObjects/Towers";

    [Tooltip("필수: 켜고 끌 서브패널 루트. 이 컴포넌트가 붙은 오브젝트 자신이면 안 된다.")]
    [SerializeField] GameObject _panelRoot;

    [Tooltip("필수: 행을 담을 부모(서브패널 ScrollRect의 Content).")]
    [SerializeField] Transform _content;

    [Tooltip("필수: DebugRowButton 프리팹(Button + 자식 TMP_Text). 스킬 패널과 공용.")]
    [SerializeField] GameObject _rowPrefab;

    [Tooltip("선택: 비우면 씬에서 찾는다.")]
    [SerializeField] TowerPlacer _placer;

    TowerPlacer _resolvedPlacer;

    TowerPlacer Placer
    {
        get
        {
            if (_resolvedPlacer == null)
            {
                _resolvedPlacer = _placer != null ? _placer : FindFirstObjectByType<TowerPlacer>();
            }
            return _resolvedPlacer;
        }
    }

    void Start()
    {
        // 먼저 만들고 나중에 숨긴다 - 생성 시점엔 계층이 활성이라 레이아웃이 정상 계산된다.
        BuildRows();

        if (_panelRoot != null) _panelRoot.SetActive(false);
        else Debug.LogError("[타워디버그] _panelRoot가 연결되지 않았습니다.");
    }

    // Btn_Tower의 onClick에 연결한다.
    public void Toggle()
    {
        if (_panelRoot == null) return;

        _panelRoot.SetActive(!_panelRoot.activeSelf);
    }

    // 타워 SO는 정적이라 런타임에 변하지 않는다 - Start에서 한 번만 만든다.
    void BuildRows()
    {
        if (_content == null || _rowPrefab == null)
        {
            Debug.LogError("[타워디버그] Content/행 프리팹이 연결되지 않았습니다.");
            return;
        }

        TowerAsset[] towers = Resources.LoadAll<TowerAsset>(k_TowerFolder);
        if (towers.Length == 0)
        {
            Debug.LogWarning($"[타워디버그] '{k_TowerFolder}'에서 타워를 찾지 못했습니다.");
            return;
        }

        // LoadAll 순서는 보장되지 않는다 - 실행마다 목록이 흔들리지 않게 이름순 고정
        // (TowerRecipeDebugPanel과 같은 규약).
        Array.Sort(towers, (a, b) => string.CompareOrdinal(a.name, b.name));

        TowerTable table = DataTableManager.Get<TowerTable>("TowerTable");

        foreach (TowerAsset tower in towers)
        {
            if (tower == null) continue;

            // Data는 에셋에 저장되지 않는 런타임 캐시다. SO를 주입하는 쪽이 CSV에서 채우는 규약이라
            // (TowerSelectPanelView.AddTowerButton) 여기서도 같은 일을 해야 TowerPlacer가 받아준다.
            // 빈 ID는 먼저 걸러낸다 - TowerTable.Get이 미스 시 불필요한 LogError를 낸다.
            if (tower.Data == null && !string.IsNullOrEmpty(tower.TowerID))
            {
                tower.Data = table?.Get(tower.TowerID);
            }

            GameObject row = Instantiate(_rowPrefab, _content);
            Button button = row.GetComponentInChildren<Button>();
            TMP_Text label = row.GetComponentInChildren<TMP_Text>();

            if (button == null)
            {
                Debug.LogError($"[타워디버그] 행 프리팹에 Button이 없습니다: {tower.name}", row);
                continue;
            }

            // 배치가 구조적으로 불가능한 SO는 눌러봐야 TowerPlacer가 LogError만 낸다.
            // 버튼을 죽이고 이유를 라벨에 붙여 저작 문제를 그 자리에서 드러낸다.
            string blocked = BlockReason(tower);
            if (blocked != null)
            {
                button.interactable = false;
                if (label != null) label.text = $"{Label(tower)} - {blocked}";
                continue;
            }

            if (label != null) label.text = Label(tower);

            TowerAsset captured = tower;   // 클로저가 루프 변수를 잡지 않도록 복사
            button.onClick.AddListener(() => Place(captured));
        }
    }

    void Place(TowerAsset tower)
    {
        TowerPlacer placer = Placer;
        if (placer == null)
        {
            Debug.LogWarning("[타워디버그] TowerPlacer가 씬에 없습니다.");
            return;
        }

        // cost: null이 무료화의 전부다. historyOwner는 Placer - 일반 배치와 똑같이
        // 되돌리기 히스토리에 올라가되, 지불 기록이 비어 있어 Undo 환원도 0이 된다.
        if (!placer.BeginTowerPlacement(tower, null, null, null, PlacementOwner.Placer))
        {
            // 세션이 시작되지 않았으면 패널을 닫을 이유가 없다(반환값 계약 - TowerPlacer.cs:187 참고).
            return;
        }

        // 고스트를 조작해야 하므로 목록을 비켜준다. 메인 패널(F4)은 그대로 둔다.
        if (_panelRoot != null) _panelRoot.SetActive(false);

        Debug.Log($"[타워디버그] 무료 배치 시작: {tower.TowerID}");
    }

    // TowerPlacer가 거부하는 조건을 미리 판정한다(TowerPlacer.cs의 Data/프리팹 가드와 대응).
    static string BlockReason(TowerAsset tower)
    {
        if (tower.Data == null) return "데이터 없음";
        if (tower.TowerPrefab == null || tower.GhostPrefab == null) return "프리팹 없음";
        return null;
    }

    // TowerID -> TowerData.NameKey -> NorthLand_Towers 로컬라이즈.
    // 조회에 실패하면 TowerID를 그대로 쓴다 - 빈 버튼이 뜨는 것보다 낫고, 어느 SO가 문제인지도 드러난다.
    static string Label(TowerAsset tower)
    {
        if (tower.Data == null || string.IsNullOrEmpty(tower.Data.NameKey)) return tower.TowerID;

        // 로케일이 아직 초기화되지 않았으면 null이 온다(EditMode에서 재현됨).
        string localized = LocalizationHelper.Get(LocalizationHelper.k_TowersTable, tower.Data.NameKey);
        return string.IsNullOrEmpty(localized) ? tower.TowerID : localized;
    }
}