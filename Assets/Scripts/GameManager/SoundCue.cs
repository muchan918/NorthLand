using UnityEngine;

/// <summary>
/// 씬이 소유하는 사운드 큐의 공통 베이스. (#361)
///
/// <see cref="AudioManager"/>는 DontDestroyOnLoad라 인스펙터 배선을 가질 수 없어 재생 엔진만 맡는다.
/// "이 씬에서 무엇을 틀지"는 씬 쪽인 큐가 정한다 — 클립 배선과 이벤트 구독이 여기에 있다.
///
/// 씬마다 정확히 하나의 큐가 답을 갖는다는 것이 이 계층의 계약이다. 매니저가 씬을 넘어 살아남으므로
/// **답을 갖지 않는 씬은 직전 씬의 BGM을 그대로 끌고 간다** — 타이틀이 그 사고였다(WL-180).
/// </summary>
public abstract class SoundCue : MonoBehaviour
{
    [SerializeField]
    [Tooltip("트랙 교체 크로스페이드 길이(초).")]
    private float fadeSeconds = 1f;

    protected float FadeSeconds => fadeSeconds;

    /// <summary>
    /// 씬이 시작되면 직전 씬에서 울리던 <b>끊기지 않는 원샷</b>(<see cref="AudioManager.PlaySfxExclusive"/>)을
    /// 멈춘다.
    ///
    /// BGM 정지 주체를 씬에 둔 것과 **같은 이유**다(§5.1) — 매니저가 씬을 넘어 살아남으므로, 정지를
    /// 전환 경로마다 붙이면 경로가 넷(게임오버·클리어·복원 실패·설정의 "메인으로")이라 어느 하나를
    /// 고치는 것으로 닫히지 않는다. 씬마다 큐가 하나씩 있다는 기존 계약에 얹으면 경로 수와 무관해진다.
    ///
    /// 짧은 원샷(<c>PlaySfx</c>)은 대상이 아니다. 그쪽은 알아서 끝나고, 씬 전환 직전에 울린 클릭음이
    /// 다음 씬 첫 프레임까지 조금 남는 것은 오히려 자연스럽다.
    ///
    /// ⚠ <b>파생 큐가 <c>Awake</c>를 선언하면 이것이 불리지 않는다.</b> 현재 <see cref="TitleCue"/>·
    /// <see cref="InGameCue"/>는 <c>Start</c>만 쓴다. 파생에서 <c>Awake</c>가 필요해지면 그쪽에서
    /// 이 정지를 직접 부를 것.
    /// </summary>
    private void Awake()
    {
        // `?.`는 Unity의 == 오버로드를 우회해 파괴된 객체를 살아 있는 것처럼 다룬다(UNT0008).
        if (AudioManager.Instance == null)
        {
            return;
        }

        AudioManager.Instance.StopSfxExclusive();
    }

    /// <summary>
    /// BGM을 요청한다. 클립이 비어 있으면 매니저가 조용히 무시하므로 **현재 트랙이 그대로 유지된다** —
    /// "아무것도 틀지 않는다"를 원하면 <see cref="StopBgm"/>를 써야 한다.
    /// </summary>
    protected void PlayBgm(AudioClip clip)
    {
        // `?.`는 Unity의 == 오버로드를 우회해 파괴된 객체를 살아 있는 것처럼 다룬다(UNT0008).
        if (AudioManager.Instance == null)
        {
            return;
        }

        AudioManager.Instance.PlayBgm(clip, fadeSeconds);
    }

    protected void StopBgm()
    {
        if (AudioManager.Instance == null)
        {
            return;
        }

        AudioManager.Instance.StopBgm(fadeSeconds);
    }

    protected void PlaySfx(AudioClip clip, float volumeScale)
    {
        if (AudioManager.Instance == null)
        {
            return;
        }

        AudioManager.Instance.PlaySfx(clip, volumeScale);
    }
}
