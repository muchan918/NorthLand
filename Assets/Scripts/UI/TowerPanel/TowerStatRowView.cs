using TMPro;
using UnityEngine;
using NorthLand.Combat;

/// <summary>
/// 정보 패널 스탯 블록의 한 행(#536). <see cref="TowerInfoUI"/>가 선택 시점에 <see cref="Set"/>으로 채운다.
/// 프리팹 정본은 <c>@NorthLand/Prefabs/UI/TowerStatRow.prefab</c>(Imported)이고 자식은
/// <c>Stat</c> / <c>OriginStat</c> / <c>Arrow</c> / <c>BuffedStat</c> 넷이다.
///
/// <para>행은 <b>무엇을 그릴지 모른다</b> — 라벨 키도 숫자 서식도 <see cref="TowerStatRowData"/>가 들고 오고,
/// 그 값은 액션이 만든다(`TowerAction.DescribeStatRows`). 뷰가 스탯 종류를 알기 시작하면 축이 늘 때마다
/// 여기도 같이 고쳐야 한다.</para>
///
/// <para><b>버프가 없으면 화살표와 버프값 칸을 끈다.</b> <c>18 → 18</c>은 정보가 아니라 노이즈이고,
/// 행 넷 중 버프 받은 축이 하나뿐일 때 그 하나가 눈에 들어와야 한다.</para>
/// </summary>
[DisallowMultipleComponent]
public class TowerStatRowView : MonoBehaviour
{
    [Tooltip("스탯 이름 (Stat) — 로컬라이즈 키는 TowerStatRowData가 들고 온다.")]
    [SerializeField] TMP_Text _label;
    [Tooltip("기본값 (OriginStat) — 원장을 타지 않은 SO 저작값.")]
    [SerializeField] TMP_Text _origin;
    [Tooltip("화살표 (Arrow) — 버프가 있을 때만 그린다. 칸 자체는 항상 자리를 지킨다.")]
    [SerializeField] TMP_Text _arrow;
    [Tooltip("버프 적용값 (BuffedStat) — 버프가 있을 때만 켠다.")]
    [SerializeField] TMP_Text _buffed;

    // 배선 유실 경고는 **세션당 1회**다. 행은 타워를 고를 때마다 전부 다시 채워지므로 인스턴스
    // 플래그로는 선택할 때마다 같은 경고가 쏟아진다(TowerButtonView.s_bannerWiringWarned와 같은 규약).
    static bool s_wiringWarned;

    /// <summary>행을 채우고 켠다.</summary>
    public void Set(in TowerStatRowData row)
    {
        WarnIfUnwired();

        if (_label != null) _label.text = row.Label;
        if (_origin != null) _origin.text = row.BaseText;

        bool buffed = row.HasBuffedValue;

        // ⚠ **`SetActive`로 끄지 않는다.** 자식을 비활성화하면 레이아웃에서 통째로 빠져 행 폭이 줄고
        // (실측 278 → 174), 그 행의 스탯명·기본값이 화면에서 24.9px 옆으로 밀린다. 램프업 타워는
        // 전투 중에 스택이 오르내리며 이 전이가 반복되므로 열이 계속 흔들린다.
        //
        // 대신 **그리기만 끈다** — 각 칸에 `LayoutElement`(화살표 32 / 버프값 72)가 따로 붙어 있어
        // `Graphic`을 꺼도 폭 기여는 그대로 남는다. 그래서 버프가 붙든 없든 칸 위치가 고정된다.
        // 켜고 끄는 것을 매번 **양쪽 다** 세운다 — 직전 타워에서 꺼둔 칸이 그대로 남지 않게.
        if (_arrow != null) _arrow.enabled = buffed;
        if (_buffed != null)
        {
            _buffed.enabled = buffed;
            if (buffed) _buffed.text = row.BuffedText;
        }

        gameObject.SetActive(true);
    }

    /// <summary>이 행을 쓰지 않는다(표시할 스탯이 행 수보다 적을 때). 패널이 GameObject를 직접 만지지 않도록 여기 둔다.</summary>
    public void Hide() => gameObject.SetActive(false);

    // 프리팹이 별 저장소(NorthLand-Imported)에 있어 미동기 환경에서는 필드가 빈 채로 들어온다.
    // 그 경우 행이 조용히 비어 보이기만 해서 "왜 내 화면만"으로 나타난다(#445가 겪은 경로).
    void WarnIfUnwired()
    {
        if (s_wiringWarned) return;
        if (_label != null && _origin != null && _arrow != null && _buffed != null) return;

        s_wiringWarned = true;
        Debug.LogWarning("[타워정보] TowerStatRow의 슬롯이 일부 배선되지 않았습니다 — 그 칸이 빈 채로 남습니다. " +
                         "NorthLand-Imported의 TowerStatRow.prefab 동기화를 확인하세요.", this);
    }
}
