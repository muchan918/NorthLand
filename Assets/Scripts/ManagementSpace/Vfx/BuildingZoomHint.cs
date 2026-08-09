using UnityEngine;

/// <summary>
/// 줌 아웃 상태에서 "여기 상호작용 가능한 건물이 있다"를 파티클로 상시 안내한다(#138).
/// 오쏘 사이즈가 커지면 건물 실루엣이 배경과 뭉개져 어느 것이 클릭 가능한지 읽히지 않는 문제를 노린다.
/// 건물 루트(파티클이 매달린 <c>Particles</c> 노드가 있는 곳)에 붙인다.<br/>
/// <br/>
/// 켜지는 구간·구독·초기 pull·멱등·정리는 전부 <see cref="ZoomDrivenVisibility"/>가 담당하고,
/// 재생 규약은 <see cref="FeedbackParticle"/>과 공유한다. 이 클래스에 남는 것은 "무엇을 켤 것인가"뿐이다.<br/>
/// <br/>
/// <b><see cref="BuildingFeedback"/>과 분리한 이유</b>: 그쪽은 "방금 무슨 일이 일어났다"는 <b>행동의 메아리</b>(1회성)이고
/// 이쪽은 "이 건물은 상호작용 가능하다"는 <b>상태 표시</b>(상시)다. 수명도 다르고 파티클 규약도 반대라
/// (Looping off ↔ on) 한 테이블에 담으면 서로의 필드가 절반씩 죽은 값이 된다.<br/>
/// <br/>
/// ⚠ 파티클은 구간 안에 있는 내내 떠 있어야 하므로 <b>Looping을 켜고</b> Play On Awake는 끈다.
/// </summary>
[DisallowMultipleComponent]
public class BuildingZoomHint : ZoomDrivenVisibility
{
    [Tooltip("줌 구간 안에서 재생할 파티클(비워두면 아무 일도 하지 않는다 — 연출 미정 상태를 그대로 표현). " +
             "Looping을 켜 둘 것.")]
    [SerializeField] ParticleSystem _effect;

    protected override void ApplyVisible(bool visible)
    {
        if (visible)
        {
            FeedbackParticle.PlayFromStart(_effect, this);
        }
        else
        {
            FeedbackParticle.StopGracefully(_effect);
        }
    }
}
