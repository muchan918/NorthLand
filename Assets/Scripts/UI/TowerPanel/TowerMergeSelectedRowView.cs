using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 합성 패널 상단 선택 리스트 한 행의 위젯 참조(#535). <see cref="TowerMergePanelView"/>가 행을
/// 생성한 직후 <see cref="Set"/>로 채운다. 역할은 <see cref="TowerButtonView"/>와 같다 — 행은
/// 아이콘과 이름을 그리기만 하고, 무엇을 그릴지는 패널이 정한다.
///
/// <para><b>왜 <c>GetComponentInChildren</c>이 아니라 직렬화 필드인가</b>: 행 루트에 배경 Graphic이
/// 붙는 순간 <c>GetComponentInChildren&lt;Image&gt;</c>는 아이콘이 아니라 그 배경을 먼저 집는다 —
/// 컴파일도 통하고 콘솔도 조용한 채 아이콘만 사라진다. <see cref="TowerButtonView"/>가 아이콘을
/// <c>Button.targetGraphic</c>에 그리지 않고 별도 슬롯으로 든 것과 같은 이유다.</para>
/// </summary>
[DisallowMultipleComponent]
public class TowerMergeSelectedRowView : MonoBehaviour
{
    [Tooltip("타워 아이콘 (행의 첫 번째 자식 — 맨 왼쪽)")]
    [SerializeField] Image _icon;
    [Tooltip("타워 이름 (TowerName)")]
    [SerializeField] TMP_Text _label;

    // 배선 유실 경고는 **세션당 1회**다. 행은 선택한 타워 수만큼 생기고 선택이 바뀔 때마다 다시
    // 생성되므로, 인스턴스 플래그로는 같은 경고가 매 갱신마다 쏟아진다
    // (TowerButtonView.s_bannerWiringWarned와 같은 규약·같은 이유).
    static bool s_wiringWarned;

    /// <summary>
    /// 행을 채운다. 아이콘이 없는 타워는 흰 사각형 대신 빈 칸으로 둔다
    /// (<see cref="TowerButtonView.Set"/>·ResourceAsset 계보와 같은 규약).
    /// </summary>
    public void Set(Sprite icon, string label)
    {
        WarnIfUnwired();

        if (_icon != null)
        {
            _icon.enabled = icon != null;
            _icon.sprite = icon;
        }

        if (_label != null) _label.text = label ?? string.Empty;
    }

    // 프리팹이 별 저장소(NorthLand-Imported)에 있어 미동기 환경에서는 필드가 빈 채로 들어온다.
    // 그 경우 행이 조용히 비어 보이기만 해서 "왜 내 화면만"으로 나타난다(#445가 겪은 경로).
    void WarnIfUnwired()
    {
        if (s_wiringWarned) return;
        if (_icon != null && _label != null) return;

        s_wiringWarned = true;
        string missing = _icon == null ? (_label == null ? "_icon·_label" : "_icon") : "_label";
        Debug.LogWarning($"[타워합성] SelectedRow의 {missing}가 배선되지 않았습니다 — 해당 칸이 빈 채로 남습니다. " +
                         $"NorthLand-Imported의 SelectedRow.prefab 동기화를 확인하세요. ({name})", this);
    }
}
