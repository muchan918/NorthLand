using NorthLand.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 업그레이드 비용 한 줄(자원 1종).
// BuildingInfoUI가 ScrollView Content에 Instantiate한 뒤 Set으로 주입한다.
// 자원 종류는 이름 텍스트가 아니라 아이콘으로만 표기한다(ResourceAsset.Icon).
// 지갑 보유량이 충분하면 초록, 부족하면 회색으로 아이콘·수량 색을 바꾼다.
public class BuildingCostRow : MonoBehaviour
{
    [Tooltip("자원 아이콘 (프리팹의 Img_Icon)")]
    [SerializeField]
    private Image _icon;

    [Tooltip("필요 수량 텍스트 (프리팹의 Txt_Amount)")]
    [SerializeField]
    private TextMeshProUGUI _amount;

    [Tooltip("보유량이 부족할 때 수량 텍스트 색")]
    [SerializeField]
    private Color _insufficientColor =
        new Color(0.55f, 0.55f, 0.55f);

    // 아이콘은 자원 그림이라 텍스트와 같은 초록을 입히면
    // 스프라이트가 물든다. 충족 시 원본 색을 사용하고,
    // 부족할 때만 회색으로 표시한다.
    [Tooltip("보유량이 충분할 때 아이콘 tint (흰색 = 스프라이트 원본)")]
    [SerializeField]
    private Color _iconAffordableColor = Color.white;

    [Tooltip("보유량이 부족할 때 아이콘 tint")]
    [SerializeField]
    private Color _iconInsufficientColor =
        new Color(0.55f, 0.55f, 0.55f);

    /// <summary>
    /// 비용 한 줄을 채운다.
    /// </summary>
    /// <param name="icon">
    /// 자원 아이콘. 미할당이면 이미지를 숨긴다.
    /// </param>
    /// <param name="amount">필요한 자원 수량.</param>
    /// <param name="affordable">
    /// 해당 비용을 감당할 수 있으면 true.
    /// </param>
    public void Set(Sprite icon, int amount, bool affordable)
    {
        if (_icon != null)
        {
            _icon.enabled = icon != null;
            _icon.sprite = icon;
            _icon.color = affordable? _iconAffordableColor: _iconInsufficientColor;
        }

        if (_amount != null)
        {
            _amount.text = amount.ToString();
            _amount.color = affordable ? UiPalette.Positive: _insufficientColor;
        }
    }
}