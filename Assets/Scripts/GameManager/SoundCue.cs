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
