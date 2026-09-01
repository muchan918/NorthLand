using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NorthLand.UI;

// 교환 상점의 한 줄(자원 1종). StorePanelUI가 ScrollView Content에 Instantiate한 뒤 Bind로 주입한다.
// BuildingCostRow와 같은 계보의 얇은 뷰지만, 버튼이 있어 '상태만 갱신'이 필요하다 —
// 갱신은 행 재생성이 아니라 SetInteractable로 한다(클릭 도중 행이 파괴되면 안 되므로, StorePanelUI 주석 참고).
// (프리팹 StoreOfferRow의 Cost/Gain 텍스트와 Button을 인스펙터에서 연결할 것)
public class StoreOfferRow : MonoBehaviour, IDisabledClickFeedback
{
    [Tooltip("지불 자원 아이콘 (마나석)")]
    [SerializeField] Image _payIcon;
    [Tooltip("지불 수량")]
    [SerializeField] TextMeshProUGUI _costText;
    [Tooltip("획득 자원 아이콘")]
    [SerializeField] Image _gainIcon;
    [Tooltip("획득 수량")]
    [SerializeField] TextMeshProUGUI _gainText;
    [Tooltip("교환 버튼")]
    [SerializeField] Button _exchangeButton;
    [Tooltip("교환 버튼의 라벨 (로컬라이즈된 '교환')")]
    [SerializeField] TextMeshProUGUI _buttonLabel;

    [Tooltip("지불 자원이 부족할 때 글씨 색")]
    [SerializeField] Color _insufficientColor = new Color(0.55f, 0.55f, 0.55f);

    private Action _onExchange;
    // 교환 버튼이 죽은 사유가 **지불 자원 부족 하나뿐인지**. 소리 전용 값이다(OnDisabledClick).
    private bool _blockedByCost;

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
    public void Bind(Sprite payIcon, int payAmount, Sprite gainIcon, int gainAmount, string buttonText, Action onExchange)
    {
        _onExchange = onExchange;

        SetIcon(_payIcon, payIcon);
        SetIcon(_gainIcon, gainIcon);
        if (_costText != null)
        {
            _costText.text = payAmount.ToString();
        }
        SetGain(gainAmount);
        if (_buttonLabel != null)
        {
            _buttonLabel.text = buttonText;
        }
    }

    // 아이콘 미할당 SO를 흰 사각형으로 그리지 않는다 — BuildingCostRow와 같은 규약.
    private static void SetIcon(Image target, Sprite icon)
    {
        if (target == null)
        {
            return;
        }
        target.enabled = icon != null;
        target.sprite = icon;
    }

    /// <summary>
    /// 획득량 표시만 갱신한다(#229). 본진 레벨이 오르면 교환 효율 배율이 바뀌므로, 상점을 연 채로도
    /// 이 값이 따라 움직여야 표시와 실지급이 어긋나지 않는다.<br/>
    /// 행 재생성이 아니라 갱신인 이유는 <see cref="SetInteractable"/>과 같다 — 클릭 처리 도중 행이 파괴되면 안 된다.
    /// </summary>
    public void SetGain(int gainAmount)
    {
        if (_gainText != null)
        {
            _gainText.text = gainAmount.ToString();
        }
    }

    /// <summary>
    /// 교환 가능 여부를 반영한다(StorePanelUI가 컨트롤러 상태 변화마다 호출).<br/>
    /// <paramref name="blockedByCost"/>는 <b>소리 전용</b>이다 — 버튼이 죽은 사유가 지불 자원 부족
    /// 하나뿐일 때만 true이며, <see cref="OnDisabledClick"/>이 이 값으로 안내음을 낼지 정한다.
    /// 사유 판정은 게이트를 계산한 <c>StorePanelUI</c>가 소유한다.
    /// </summary>
    public void SetInteractable(bool canExchange, bool blockedByCost)
    {
        if (_exchangeButton != null)
        {
            _exchangeButton.interactable = canExchange;
        }
        _blockedByCost = blockedByCost;

        if (_costText != null)
        {
            _costText.color = canExchange? UiPalette.Positive : _insufficientColor;
        }
        if (_payIcon != null)
        {
            // 아이콘은 자원 그림이라 초록으로 물들이지 않는다 — 부족할 때만 회색으로 죽인다(BuildingCostRow와 동일).
            _payIcon.color = canExchange ? Color.white : _insufficientColor;
        }
    }

    private void HandleClicked() => _onExchange?.Invoke();

    /// <summary>
    /// 회색이 된 교환 버튼을 눌렀을 때(<see cref="IDisabledClickFeedback"/>). 지불 자원 부족이
    /// 유일한 사유일 때만 안내음을 낸다 — 밤 페이즈·튜토리얼 제한은 기다려도 자원이 느는 문제가
    /// 아니라 "모으면 된다"가 거짓 안내가 된다(<see cref="Sfx.InsufficientResources"/> 주석).
    ///
    /// ⚠ 이 컴포넌트는 <b>행 루트</b>에 있고 눌리는 <c>Button</c>은 그 자식이다. 훅이 닿는 것은
    /// <see cref="UiClickSfx"/>가 눌린 <c>Selectable</c>에서 <b>부모로 올라가며</b> 구현체를 찾기
    /// 때문이다 — 그 탐색을 좁히면 이 행이 다시 무음이 된다.
    ///
    /// ⚠ 게임 상태를 바꾸지 않는다(<see cref="IDisabledClickFeedback"/>의 제약).
    /// </summary>
    public void OnDisabledClick(Selectable pressed)
    {
        if (!_blockedByCost) return;

        Sfx.InsufficientResources();
    }
}
