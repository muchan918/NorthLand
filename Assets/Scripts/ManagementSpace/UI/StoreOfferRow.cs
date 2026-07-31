using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 교환 상점의 한 줄(자원 1종). StorePanelUI가 ScrollView Content에 Instantiate한 뒤 Bind로 주입한다.
// BuildingCostRow와 같은 계보의 얇은 뷰지만, 버튼이 있어 '상태만 갱신'이 필요하다 —
// 갱신은 행 재생성이 아니라 SetInteractable로 한다(클릭 도중 행이 파괴되면 안 되므로, StorePanelUI 주석 참고).
// (프리팹 StoreOfferRow의 Cost/Gain 텍스트와 Button을 인스펙터에서 연결할 것)
public class StoreOfferRow : MonoBehaviour
{
    [Tooltip("지불 자원 텍스트 (예: 마나석 10)")]
    [SerializeField] TextMeshProUGUI _costText;
    [Tooltip("획득 자원 텍스트 (예: → 나무 30)")]
    [SerializeField] TextMeshProUGUI _gainText;
    [Tooltip("교환 버튼")]
    [SerializeField] Button _exchangeButton;
    [Tooltip("교환 버튼의 라벨 (로컬라이즈된 '교환')")]
    [SerializeField] TextMeshProUGUI _buttonLabel;

    [Tooltip("지불 자원을 감당할 수 있을 때 글씨 색")]
    [SerializeField] Color _affordableColor = new Color(0.45f, 0.85f, 0.45f);
    [Tooltip("지불 자원이 부족할 때 글씨 색")]
    [SerializeField] Color _insufficientColor = new Color(0.55f, 0.55f, 0.55f);

    private Action _onExchange;

    private void Awake()
    {
        if (_exchangeButton != null)
        {
            _exchangeButton.onClick.AddListener(HandleClicked);
        }
    }

    private void OnDestroy()
    {
        if (_exchangeButton != null)
        {
            _exchangeButton.onClick.RemoveListener(HandleClicked);
        }
    }

    /// <summary>
    /// 교환 한 줄을 채운다(생성 직후 1회). 표시 문자열은 이미 로컬라이즈된 자원명을 받는다.<br/>
    /// <paramref name="onExchange"/>는 버튼 클릭 시 호출된다 — 실제 차감/지급은 컨트롤러가 한다(뷰는 로직 없음).
    /// </summary>
    public void Bind(string payName, int payAmount, string gainName, int gainAmount, string buttonText, Action onExchange)
    {
        _onExchange = onExchange;

        if (_costText != null)
        {
            _costText.text = $"{payName} {payAmount}";
        }
        SetGain(gainName, gainAmount);
        if (_buttonLabel != null)
        {
            _buttonLabel.text = buttonText;
        }
    }

    /// <summary>
    /// 획득량 표시만 갱신한다(#229). 본진 레벨이 오르면 교환 효율 배율이 바뀌므로, 상점을 연 채로도
    /// 이 값이 따라 움직여야 표시와 실지급이 어긋나지 않는다.<br/>
    /// 행 재생성이 아니라 갱신인 이유는 <see cref="SetInteractable"/>과 같다 — 클릭 처리 도중 행이 파괴되면 안 된다.
    /// </summary>
    public void SetGain(string gainName, int gainAmount)
    {
        if (_gainText != null)
        {
            _gainText.text = $"→ {gainName} {gainAmount}";
        }
    }

    /// <summary>교환 가능 여부를 반영한다(StorePanelUI가 컨트롤러 상태 변화마다 호출).</summary>
    public void SetInteractable(bool canExchange)
    {
        if (_exchangeButton != null)
        {
            _exchangeButton.interactable = canExchange;
        }
        if (_costText != null)
        {
            _costText.color = canExchange ? _affordableColor : _insufficientColor;
        }
    }

    private void HandleClicked() => _onExchange?.Invoke();
}
