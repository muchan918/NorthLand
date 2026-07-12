using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 생산 라인 한 행의 뷰. 자원 이름·배치 주민 수·예상 생산량을 표시하고 +/- 버튼으로
/// 컨트롤러에 주민 배치/회수를 요청한다. 위젯 참조는 SerializeField로만 갖는다 —
/// 실제 UI 아트로 교체 시 이 참조만 다시 연결하면 된다.<br/>
/// (Docs/ManagementArea/Resources.md — 이슈 #43)
/// </summary>
public class ProductionLineView : MonoBehaviour
{
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _villagerText;
    [SerializeField] private TMP_Text _expectedText;
    [SerializeField] private Button _plusButton;
    [SerializeField] private Button _minusButton;

    private ManagementController _controller;
    private int _lineIndex;

    public void Bind(ManagementController controller, int lineIndex)
    {
        _controller = controller;
        _lineIndex = lineIndex;

        if (_plusButton != null)
        {
            _plusButton.onClick.RemoveAllListeners();
            _plusButton.onClick.AddListener(() => _controller.AssignVillager(_lineIndex));
        }
        if (_minusButton != null)
        {
            _minusButton.onClick.RemoveAllListeners();
            _minusButton.onClick.AddListener(() => _controller.UnassignVillager(_lineIndex));
        }
    }

    public void Refresh()
    {
        if (_controller == null)
        {
            return;
        }

        if (_nameText != null)
        {
            // 임시로 영어 표기(ResourceKind 이름) — 한글 폰트 도입 전까지. 한글 DisplayName은 LineDisplayName 참고.
            _nameText.text = _controller.LineKind(_lineIndex).ToString();
        }
        if (_villagerText != null)
        {
            _villagerText.text = _controller.LineVillagers(_lineIndex).ToString();
        }
        if (_expectedText != null)
        {
            _expectedText.text = $"+{_controller.LineExpectedProduction(_lineIndex)}";
        }

        bool editable = _controller.IsDay;
        if (_plusButton != null)
        {
            _plusButton.interactable = editable;
        }
        if (_minusButton != null)
        {
            _minusButton.interactable = editable;
        }
    }
}
