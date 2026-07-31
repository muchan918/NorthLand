using TMPro;
using UnityEngine;

// 업그레이드 비용 한 줄(자원 1종). BuildingInfoUI가 ScrollView Content에 Instantiate한 뒤 Set으로 주입한다.
// 지갑 보유량이 충분하면 초록, 부족하면 회색으로 글씨 색을 바꿔 시인성을 준다.
// (프리팹 BuildingCostRow의 Label/Amount 텍스트를 인스펙터에서 연결할 것)
public class BuildingCostRow : MonoBehaviour
{
    [Tooltip("자원 이름 텍스트 (프리팹의 Label)")]
    [SerializeField] TextMeshProUGUI _label;
    [Tooltip("필요 수량 텍스트 (프리팹의 Amount)")]
    [SerializeField] TextMeshProUGUI _amount;

    [Tooltip("보유량이 충분할 때 글씨 색")]
    [SerializeField] Color _affordableColor = new Color(0.45f, 0.85f, 0.45f);
    [Tooltip("보유량이 부족할 때 글씨 색")]
    [SerializeField] Color _insufficientColor = new Color(0.55f, 0.55f, 0.55f);

    /// <summary>
    /// 비용 한 줄을 채운다.<br/>
    /// <paramref name="affordable"/> = 지갑 보유량이 이 자원 비용을 감당하는지(true=초록, false=회색).
    /// </summary>
    public void Set(string resourceName, int amount, bool affordable)
    {
        Color color = affordable ? _affordableColor : _insufficientColor;

        if (_label != null)
        {
            _label.text = resourceName;
            _label.color = color;
        }
        if (_amount != null)
        {
            _amount.text = amount.ToString();
            _amount.color = color;
        }
    }
}
