using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 생산/자원 패널의 한 행 뷰. 열 구성(#166 ProdRow): <b>이름 · 지갑(보유량) · 주민수+버튼 · +n(오늘 밤 지나면 증가분)</b>.<br/>
/// 세 모드로 분기(프리팹은 공유):<br/>
/// - <b>Villager</b>: 기본 자원 생산 라인(나무·철·식량). 주민수 칸+버튼 표시, +n = 예상 생산량.<br/>
/// - <b>Supply</b>: 미개척 영지 자원(금/루비/사파이어/다이아). 주민수 칸·버튼 숨김, +n = 일일 수급량,
///   <b>수급량 0이면 회색</b>("이런 게 있구나" 정도).<br/>
/// - <b>Mana</b>: 마나석. 주민수 칸·버튼 숨김, +n = 웨이브 클리어 마나 미리보기, 항상 정상.<br/>
/// 지갑(보유량)은 <b>모든 행</b>에 표시된다(종전 탑 바 지갑 표기를 행으로 이관, #166·Resources.md §5.5 (C)).<br/>
/// 위젯 참조는 SerializeField로만 갖는다 — UI 아트 교체 시 이 참조만 다시 연결.<br/>
/// (Docs/ManagementArea/Resources.md §5.5 — 이슈 #166)
/// </summary>
public class ProductionLineView : MonoBehaviour
{
    private enum RowMode { Villager, Supply, Mana }

    [SerializeField] TMP_Text _nameText;
    [Tooltip("지갑(현재 보유량) — 모든 행 공통. 탑 바에서 이관됨(#166).")]
    [SerializeField] TMP_Text _balanceText;
    [Tooltip("배치 주민 수 — 기본 자원(Villager) 행에서만 보인다.")]
    [SerializeField] TMP_Text _villagerText;
    [Tooltip("+n: Villager=예상 생산량, Supply=일일 수급량, Mana=웨이브 클리어 마나")]
    [SerializeField] TMP_Text _expectedText;
    [SerializeField] Button _plusButton;
    [SerializeField] Button _minusButton;

    [Header("미개방(비활성) 색 (#166)")]
    [Tooltip("아직 확보하지 않은 특수 자원 행의 텍스트 색 — 회색/저알파로 '존재만' 알림. 활성 행은 프리팹 원색 유지.")]
    [SerializeField] Color _inactiveColor = new(0.6f, 0.6f, 0.6f, 0.4f);

    private ManagementController _controller;
    private RowMode _mode;
    private int _lineIndex;      // Villager 모드
    private ResourceKind _kind;  // Supply/Mana 모드
    private string _nameKey;     // Supply/Mana 모드 표시명 키

    // 활성 행이 유지할 원색(프리팹 저작값) 캐시 — Bind 시점에 1회 캡처해 회색↔원색 복원에 쓴다.
    private Color _nameColor, _balanceColor, _villagerColor, _expectedColor;
    private bool _colorsCached;

    /// <summary>기본 자원 생산 라인(주민 배치)으로 바인딩한다(#166 Villager 모드).</summary>
    public void BindVillager(ManagementController controller, int lineIndex)
    {
        _controller = controller;
        _mode = RowMode.Villager;
        _lineIndex = lineIndex;
        CacheColors();
        ConfigureVillagerUI(true);
    }

    /// <summary>자원 표시 행으로 바인딩한다(#166) — 특수 자원(Supply) 또는 마나(Mana). 주민수 칸·+/- 버튼은 숨긴다.</summary>
    public void BindResourceDisplay(ManagementController controller, ResourceKind kind, string nameKey, bool isMana)
    {
        _controller = controller;
        _mode = isMana ? RowMode.Mana : RowMode.Supply;
        _kind = kind;
        _nameKey = nameKey;
        CacheColors();
        ConfigureVillagerUI(false);
    }

    // 주민수 칸·+/- 버튼은 Villager 행에서만 보인다(#166 — 마나/특수 자원은 주민 배치 대상이 아님).
    // 지갑·이름·+n 칸은 항상 유지되어 열 정렬이 흐트러지지 않는다.
    private void ConfigureVillagerUI(bool villager)
    {
        if (_villagerText != null)
        {
            _villagerText.gameObject.SetActive(villager);
        }
        if (_plusButton != null)
        {
            _plusButton.onClick.RemoveAllListeners();
            _plusButton.gameObject.SetActive(villager);
            if (villager)
            {
                _plusButton.onClick.AddListener(() => _controller.AssignVillager(_lineIndex));
            }
        }
        if (_minusButton != null)
        {
            _minusButton.onClick.RemoveAllListeners();
            _minusButton.gameObject.SetActive(villager);
            if (villager)
            {
                _minusButton.onClick.AddListener(() => _controller.UnassignVillager(_lineIndex));
            }
        }
    }

    private void CacheColors()
    {
        if (_colorsCached)
        {
            return;
        }
        if (_nameText != null) _nameColor = _nameText.color;
        if (_balanceText != null) _balanceColor = _balanceText.color;
        if (_villagerText != null) _villagerColor = _villagerText.color;
        if (_expectedText != null) _expectedColor = _expectedText.color;
        _colorsCached = true;
    }

    /// <summary>지금 이 행이 활성인가(#166): Villager/Mana는 항상 활성, Supply는 일일 수급량 &gt; 0일 때만. 정렬(활성 위)에 쓰인다.</summary>
    public bool IsActive =>
        _mode != RowMode.Supply || (_controller != null && _controller.SupplyDaily(_kind) > 0);

    public void Refresh()
    {
        if (_controller == null)
        {
            return;
        }

        switch (_mode)
        {
            case RowMode.Villager:
                RefreshVillager();
                break;
            case RowMode.Supply:
                int daily = _controller.SupplyDaily(_kind);
                RefreshResource(_kind, daily, daily > 0);
                break;
            case RowMode.Mana:
                RefreshResource(_kind, _controller.ManaPerWaveClear, true);
                break;
        }
    }

    private void RefreshVillager()
    {
        if (_nameText != null) _nameText.text = _controller.LineDisplayName(_lineIndex);
        if (_balanceText != null) _balanceText.text = _controller.ResourceCount(_controller.LineKind(_lineIndex)).ToString();
        if (_villagerText != null) _villagerText.text = _controller.LineVillagers(_lineIndex).ToString();
        if (_expectedText != null) _expectedText.text = $"+{_controller.LineExpectedProduction(_lineIndex)}";

        bool editable = _controller.IsDay && _controller.CanAssignVillagers;
        if (_plusButton != null) _plusButton.interactable = editable;
        if (_minusButton != null) _minusButton.interactable = editable;

        ApplyColor(true); // 기본 자원 라인은 항상 정상 표기
    }

    // 자원 표시 행: 이름 + 지갑(보유량) + "+n"(다음 정산 시 수급량). active면 원색, 아니면 회색.
    private void RefreshResource(ResourceKind kind, int perDay, bool active)
    {
        if (_nameText != null) _nameText.text = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, _nameKey);
        if (_balanceText != null) _balanceText.text = _controller.ResourceCount(kind).ToString();
        if (_expectedText != null) _expectedText.text = $"+{perDay}";
        ApplyColor(active);
    }

    // 활성=프리팹 원색 복원, 비활성=회색(_inactiveColor). 표시 텍스트 전부에 적용.
    private void ApplyColor(bool active)
    {
        if (_nameText != null) _nameText.color = active ? _nameColor : _inactiveColor;
        if (_balanceText != null) _balanceText.color = active ? _balanceColor : _inactiveColor;
        if (_villagerText != null) _villagerText.color = active ? _villagerColor : _inactiveColor;
        if (_expectedText != null) _expectedText.color = active ? _expectedColor : _inactiveColor;
    }
}
