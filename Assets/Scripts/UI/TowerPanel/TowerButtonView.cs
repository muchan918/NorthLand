using Cysharp.Threading.Tasks;   // Forget() — 해제 연출은 버튼 갱신을 막지 않는다
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
    [Tooltip("잠금 오버레이 (Slot/TowerLockOverlay) — 미해금 타워에만 켠다.")]
    [SerializeField] GameObject _lockOverlay;
    [Tooltip("해제 연출. 비어 있으면 오버레이에서 찾고, 그래도 없으면 연출 없이 즉시 걷는다.")]
    [SerializeField] TowerLockUnlockEffect _unlockEffect;

    /// <summary>
    /// 잠금 오버레이 표시. **해금 여부만** 반영한다 — 자원 부족으로 버튼이 죽은 상태에까지
    /// 자물쇠를 띄우면 "아직 안 열림"과 "돈이 없음"이 같아 보인다.
    ///
    /// <para>해제 연출은 여기서 <b>전이</b>로만 발화한다 — 오버레이의 현재 표시 상태가 곧 "직전 값"이라
    /// 별도 플래그를 들지 않는다. 플래그를 들면 세이브 복원처럼 갱신이 이벤트 없이 일어나는 경로에서
    /// 표시와 플래그가 갈라진다(#424).</para>
    /// </summary>
    public void SetLocked(bool locked)
    {
        if (_lockOverlay == null) return;

        var effect = ResolveEffect();

        // 연출이 도는 중이면 이미 "열림"으로 가고 있다. activeSelf는 아직 true이므로
        // 이 가드가 없으면 재생 중 들어온 갱신마다 연출이 처음부터 다시 돈다.
        if (!locked && effect != null && effect.IsPlaying) return;

        bool wasLocked = _lockOverlay.activeSelf;
        if (locked == wasLocked) return;

        if (locked)
        {
            // 잠김으로 되돌아가는 건 정상 진행에는 없다(씬 재시작·복원뿐) — 연출 없이 즉시.
            if (effect != null) effect.SnapToLocked();
            else _lockOverlay.SetActive(true);
            return;
        }

        // 버튼 갱신이 연출을 기다릴 이유는 없다 — 던지고 빠진다.
        if (effect != null) effect.PlayAsync().Forget();
        else _lockOverlay.SetActive(false);
    }

    TowerLockUnlockEffect ResolveEffect()
    {
        if (_unlockEffect == null && _lockOverlay != null)
            _unlockEffect = _lockOverlay.GetComponent<TowerLockUnlockEffect>();
        return _unlockEffect;
    }

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
