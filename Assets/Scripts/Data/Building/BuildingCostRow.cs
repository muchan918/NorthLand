using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 업그레이드 비용 한 줄(자원 1종). BuildingInfoUI가 ScrollView Content에 Instantiate한 뒤 Set으로 주입한다.
// 자원 종류는 이름 텍스트가 아니라 아이콘으로만 표기한다(ResourceAsset.Icon) — 로케일과 무관하고 폭이 일정하다.
// 지갑 보유량이 충분하면 초록, 부족하면 회색으로 아이콘·수량 색을 바꿔 시인성을 준다.
// (프리팹 CostRow의 Img_Icon/Txt_Amount를 인스펙터에서 연결할 것)
public class BuildingCostRow : MonoBehaviour
{
    [Tooltip("자원 아이콘 (프리팹의 Img_Icon)")]
    [SerializeField] Image _icon;
    [Tooltip("필요 수량 텍스트 (프리팹의 Txt_Amount)")]
    [SerializeField] TextMeshProUGUI _amount;

    [Tooltip("보유량이 충분할 때 수량 텍스트 색")]
    [SerializeField] Color _affordableColor = new Color(0.45f, 0.85f, 0.45f);
    [Tooltip("보유량이 부족할 때 수량 텍스트 색")]
    [SerializeField] Color _insufficientColor = new Color(0.55f, 0.55f, 0.55f);

    // 아이콘은 자원 그림이라 텍스트와 같은 초록을 입히면 스프라이트가 물든다 — 충족 시 원본 색(흰색),
    // 부족할 때만 회색으로 죽인다.
    [Tooltip("보유량이 충분할 때 아이콘 tint (흰색 = 스프라이트 원본)")]
    [SerializeField] Color _iconAffordableColor = Color.white;
    [Tooltip("보유량이 부족할 때 아이콘 tint")]
    [SerializeField] Color _iconInsufficientColor = new Color(0.55f, 0.55f, 0.55f);

    /// <summary>
    /// 비용 한 줄을 채운다.<br/>
    /// <paramref name="icon"/> = 자원 아이콘(<see cref="ResourceAsset.Icon"/>). 미할당이면 이미지를 숨긴다.<br/>
    /// <paramref name="affordable"/> = 지갑 보유량이 이 자원 비용을 감당하는지(true=초록, false=회색).
    /// </summary>
    public void Set(Sprite icon, int amount, bool affordable)
    {
        if (_icon != null)
        {
            // 아이콘 미할당 SO를 흰 사각형으로 그리지 않는다 — 빈 칸이 낫다.
            _icon.enabled = icon != null;
            _icon.sprite = icon;
            _icon.color = affordable ? _iconAffordableColor : _iconInsufficientColor;
        }
        if (_amount != null)
        {
            _amount.text = amount.ToString();
            _amount.color = affordable ? _affordableColor : _insufficientColor;
        }
    }
}
