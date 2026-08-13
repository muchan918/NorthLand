using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타워 선택 버튼 한 칸의 위젯 참조. <see cref="TowerSelectPanelView"/>가 생성 직후 <see cref="Set"/>로 채운다.<br/>
/// 아이콘을 <c>Button.targetGraphic</c>에 그리지 않는 이유: targetGraphic은 테두리(Img_Frame)가 맡아야
/// 클릭 영역이 칸 전체(90x90)가 되고, 거기에 스프라이트를 덮어쓰면 테두리가 지워지기 때문이다.
/// 그래서 아이콘 슬롯을 별도 참조로 갖는다.
/// </summary>
public class TowerButtonView : MonoBehaviour
{
    [Tooltip("타워 아이콘 (Slot/Img_Icon) — 테두리 안쪽에 그려진다.")]
    [SerializeField] Image _icon;
    [Tooltip("타워 이름 (Banner/Txt_Name)")]
    [SerializeField] TMP_Text _name;

    public void Set(Sprite icon, string displayName)
    {
        if (_icon != null)
        {
            // 아이콘 미할당 SO를 흰 사각형으로 그리지 않는다 — 빈 칸이 낫다(ResourceAsset 계보와 같은 규약).
            _icon.enabled = icon != null;
            _icon.sprite = icon;
        }
        if (_name != null)
        {
            _name.text = displayName;
        }
    }
}
