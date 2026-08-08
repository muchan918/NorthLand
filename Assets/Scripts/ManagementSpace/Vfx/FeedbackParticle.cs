using UnityEngine;

/// <summary>
/// 건물 연출 파티클의 재생 규약을 한 곳에 모은 헬퍼(#138).
/// 구동원이 다른 컴포넌트(<see cref="BuildingFeedback"/> = 행동 구동, <see cref="BuildingZoomHint"/> = 줌 구동)가
/// 같은 규약을 쓰므로, 한쪽만 고쳐 어긋나지 않도록 여기 둔다.<br/>
/// <br/>
/// <b>파티클 프리팹 규약</b>: 오브젝트는 <b>켜둔 채</b> Play On Awake를 끈다 — 비활성 오브젝트에는
/// <see cref="ParticleSystem.Play(bool)"/>가 <b>예외 없이 조용히 실패</b>하기 때문에, 여기서 그 상태를
/// 경고로 잡아준다. Looping은 구동원이 정한다(1회성 연출은 끄고, 상시 표시는 켠다).
/// </summary>
public static class FeedbackParticle
{
    /// <summary>
    /// 처음부터 다시 재생한다. 직전 재생이 남아 있으면 지운다 — 연속 트리거에서 잔상이 겹치지 않게.
    /// </summary>
    /// <param name="effect">재생할 파티클. null이면 아무 일도 하지 않는다(연출 미정 슬롯은 정상 상태다).</param>
    /// <param name="context">경고 로그에서 클릭해 찾아갈 대상.</param>
    public static void PlayFromStart(ParticleSystem effect, Object context)
    {
        if (effect == null)
        {
            return;
        }

        if (!effect.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"[건물연출] {effect.name}이 비활성이라 재생할 수 없습니다. " +
                             "오브젝트는 켜두고 Play On Awake만 끄세요.", context);
            return;
        }

        effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        effect.Play(true); // withChildren — 중첩된 하위 시스템(twinkle·Smoke 등)까지 함께
    }

    /// <summary>
    /// 방출만 멈추고 이미 떠 있는 입자는 수명대로 사라지게 둔다.
    /// Clear로 지우면 조건이 바뀌는 순간 파티클이 뚝 끊겨 사라지는 것이 그대로 보인다.
    /// </summary>
    public static void StopGracefully(ParticleSystem effect)
    {
        if (effect == null)
        {
            return;
        }

        effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
