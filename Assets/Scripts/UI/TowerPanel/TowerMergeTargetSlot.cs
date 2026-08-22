using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타워 정보 패널 "합성 후보" 블록의 한 칸 — 이 타워를 재료로 쓰는 <b>상위 타워 1종</b>을 표시한다.
/// <see cref="TowerInfoUI"/>가 칸을 생성한 직후 <see cref="Set"/>으로 채운다.
/// 명세: <c>Docs/Core/TowerMerge.md</c> §8.5.
/// <br/>
/// <b>정본 프리팹(<c>@NorthLand/Prefabs/UI/TowerTargetSlot.prefab</c>)은 아이콘만 그린다 — 의도된 것이다.</b>
/// 정보 패널은 폭이 좁아 이름 배너까지 넣으면 칸이 커져 한 줄에 몇 개 안 들어가고, 블록이 패널을
/// 통째로 밀어낸다. <b>이름은 호버 툴팁이 낸다</b>(<see cref="TowerTooltipSource"/>를
/// <see cref="TowerInfoUI"/>가 런타임 부착 — 이름·역할·설명·재료가 한 번에 나온다).
/// 그래서 <see cref="_name"/>은 <b>비워두는 것이 정상</b>이고, 이름 칸을 가진 변종 프리팹을 만들 때만 배선한다.
/// <br/>
/// <b>겉모습은 배치 팔레트 칸과 같은 계보다</b> — 프리팹은 <c>TowerButton.prefab</c>을 복제해 만들고,
/// 거기서 <c>Button</c>·<see cref="TowerButtonView"/>·<c>TowerLockOverlay</c>·배너 서브트리를 떼고
/// 이 컴포넌트를 붙인다. 같은 아이콘을 같은 테두리로 보여주면서 <b>누를 수는 없는</b> 칸이다.
/// <br/>
/// <b>왜 <see cref="TowerButtonView"/>를 그대로 안 쓰는가</b>: 슬롯 구성은 같지만 그쪽
/// <c>SetLocked</c>·해제 연출은 배치 팔레트의 <b>해금</b> 개념과 한 몸이고, 여기엔 잠금이 없다.
/// 안 쓰는 개념을 끌고 오면 프리팹에 배선할 슬롯이 늘고 "왜 정보 패널에 자물쇠가 있나"를 매번 설명해야 한다.
/// <br/>
/// <b>클릭 동작은 없다(의도)</b>: 우측 패널의 최종 결정권은 스위처 하나라는 §8.1 계약을 지키려면,
/// 칸이 패널을 갈아치우는 경로를 만들지 않는 게 맞다. 상세 정보는 호버 툴팁이 맡는다 —
/// <see cref="TowerTooltipSource"/>를 <see cref="TowerInfoUI"/>가 런타임 부착하므로 프리팹 배선이 필요 없다.
/// 대신 칸 안에 <c>Raycast Target</c>이 켜진 <c>Image</c>가 하나는 있어야 호버가 잡힌다
/// (복제 원본의 <c>Slot/Img_Bg</c>가 이미 그렇다 — <c>Button</c>을 떼도 그대로 남는다).
/// </summary>
[DisallowMultipleComponent]
public class TowerMergeTargetSlot : MonoBehaviour
{
    [Tooltip("상위 타워 아이콘 (복제 원본의 Slot/Img_Icon). 미할당 SO는 빈 칸으로 둔다.")]
    [SerializeField] Image _icon;
    [Tooltip("선택 — 이름 칸을 가진 변종 프리팹만 배선한다. 정본 칸은 아이콘 전용이고 이름은 호버 툴팁이 낸다(TowerMerge.md §8.5).")]
    [SerializeField] TMP_Text _name;

    /// <summary>
    /// 칸을 채운다. 아이콘이 없으면 슬롯을 끈다. <paramref name="displayName"/>은 <b>이름 칸이 배선된
    /// 변종에서만</b> 쓰이고 정본 칸에서는 조용히 버려진다 — 호출부가 이름을 계속 넘기는 이유는
    /// <c>TowerInfoUI</c> 쪽 주석에 있다(툴팁이 읽을 <c>Data</c>를 채우는 부수효과).
    /// </summary>
    public void Set(Sprite icon, string displayName)
    {
        if (_icon != null)
        {
            // 아이콘 미할당 SO를 흰 사각형으로 그리지 않는다 — 빈 칸이 낫다(ResourceAsset·FusionTowerEntry 규약).
            _icon.enabled = icon != null;
            _icon.sprite = icon;
        }
        if (_name != null)
        {
            _name.text = displayName;
        }
    }
}
