using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 생산/자원 패널의 한 행 뷰. 열 구성(#166 ProdRow): <b>이름 · 지갑(보유량) · 주민수+버튼 · +n(오늘 밤 지나면 증가분)</b>.<br/>
/// 두 모드로 분기(프리팹은 공유):<br/>
/// - <b>Villager</b>: 기본 자원 생산 라인(나무·철·식량). 주민수 칸+버튼 표시, +n = 예상 생산량.<br/>
/// - <b>Mana</b>: 마나석. 주민수 칸·버튼 숨김, +n = 웨이브 클리어 마나 미리보기.<br/>
/// 영토 시스템 제거와 함께 특수 자원(Supply) 모드와 미개방 회색 표기는 사라졌다(#337).<br/>
/// 지갑(보유량)은 <b>모든 행</b>에 표시된다(종전 탑 바 지갑 표기를 행으로 이관, #166·Resources.md §5.5 (C)).<br/>
/// 위젯 참조는 SerializeField로만 갖는다 — UI 아트 교체 시 이 참조만 다시 연결.<br/>
/// (Docs/ManagementArea/Resources.md §5.5 — 이슈 #166·#337)
/// </summary>
public class ProductionLineView : MonoBehaviour
{
    private enum RowMode { Villager, Mana }

    [SerializeField] TMP_Text _nameText;
    [Tooltip("지갑(현재 보유량) — 모든 행 공통. 탑 바에서 이관됨(#166).")]
    [SerializeField] TMP_Text _balanceText;
    [Tooltip("배치 주민 수 — 기본 자원(Villager) 행에서만 보인다.")]
    [SerializeField] TMP_Text _villagerText;
    [Tooltip("+n: Villager=예상 생산량, Supply=일일 수급량, Mana=웨이브 클리어 마나")]
    [SerializeField] TMP_Text _expectedText;
    [SerializeField] Button _plusButton;
    [SerializeField] Button _minusButton;

    private ManagementController _controller;
    private RowMode _mode;
    private int _lineIndex;      // Villager 모드
    private ResourceKind _kind;  // Mana 모드
    private string _nameKey;     // Mana 모드 표시명 키

    /// <summary>기본 자원 생산 라인(주민 배치)으로 바인딩한다(#166 Villager 모드).</summary>
    public void BindVillager(ManagementController controller, int lineIndex)
    {
        _controller = controller;
        _mode = RowMode.Villager;
        _lineIndex = lineIndex;
        ConfigureVillagerUI(true);
    }

    /// <summary>표시 전용 자원 행(마나석)으로 바인딩한다(#166) — 주민수 칸·+/- 버튼은 숨긴다.</summary>
    public void BindResourceDisplay(ManagementController controller, ResourceKind kind, string nameKey)
    {
        _controller = controller;
        _mode = RowMode.Mana;
        _kind = kind;
        _nameKey = nameKey;
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
            case RowMode.Mana:
                RefreshResource(_kind, _controller.ManaPerWaveClear);
                break;
        }
    }

    private void RefreshVillager()
    {
        if (_nameText != null) _nameText.text = _controller.LineDisplayName(_lineIndex);
        if (_balanceText != null) _balanceText.text = _controller.ResourceCount(_controller.LineKind(_lineIndex)).ToString();
        if (_villagerText != null) _villagerText.text = _controller.LineVillagers(_lineIndex).ToString();
        if (_expectedText != null) _expectedText.text = $"+{_controller.LineExpectedProduction(_lineIndex)}";

        bool editable = _controller.IsDay; // 낮이면 배치 편집 가능(#219)
        if (_plusButton != null) _plusButton.interactable = editable;
        if (_minusButton != null) _minusButton.interactable = editable;
    }

    // 표시 전용 자원 행: 이름 + 지갑(보유량) + "+n"(다음 정산 시 수급량).
    private void RefreshResource(ResourceKind kind, int perDay)
    {
        if (_nameText != null) _nameText.text = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, _nameKey);
        if (_balanceText != null) _balanceText.text = _controller.ResourceCount(kind).ToString();
        if (_expectedText != null) _expectedText.text = $"+{perDay}";
    }
}
