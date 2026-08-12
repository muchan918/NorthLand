using UnityEngine;

/// <summary>
/// 이 씬에서 어떤 BGM을 틀지 정한다. (#361)
///
/// <see cref="AudioManager"/>는 DontDestroyOnLoad라 인스펙터 배선을 가질 수 없어
/// 크로스페이드 엔진만 맡는다. 클립 배선과 낮/밤 구독은 씬 쪽인 이 컴포넌트 몫이다.
/// 이렇게 나누면 매니저가 씬마다 죽는 <see cref="DayNightManager"/>를 재구독할 일이 없다.
///
/// 씬당 하나 배치한다. 타이틀 씬처럼 페이즈가 없는 곳은 <see cref="dayClip"/>만 배선하면 된다.
/// </summary>
public class BgmCue : MonoBehaviour
{
    [Header("Tracks")]
    [SerializeField]
    [Tooltip("낮 트랙. 페이즈가 없는 씬(타이틀 등)에서는 이 클립만 쓴다.")]
    private AudioClip dayClip;

    [SerializeField]
    [Tooltip("밤 트랙. 비워두면 페이즈 전환을 구독하지 않고 낮 트랙만 유지한다.")]
    private AudioClip nightClip;

    [SerializeField]
    [Tooltip("트랙 교체 크로스페이드 길이(초).")]
    private float fadeSeconds = 1f;

    // 구독한 대상을 그대로 들고 있다가 해제한다. 씬 파괴 순서에 따라
    // DayNightManager.Instance가 이미 갈아치워졌거나 null일 수 있다.
    private DayNightManager subscribed;

    private void Start()
    {
        DayNightManager dayNight = DayNightManager.Instance;

        if (nightClip != null && dayNight != null)
        {
            subscribed = dayNight;

            subscribed.OnDayToNight += PlayNight;
            subscribed.OnNightToDay += PlayDay;
        }

        // 초기 1회. 페이즈가 없는 씬은 항상 낮 트랙으로 시작한다.
        if (dayNight != null && dayNight.CurrentPhase == DayNightManager.Phase.Night)
        {
            PlayNight();
        }
        else
        {
            PlayDay();
        }
    }

    private void OnDestroy()
    {
        if (subscribed == null)
        {
            return;
        }

        subscribed.OnDayToNight -= PlayNight;
        subscribed.OnNightToDay -= PlayDay;

        subscribed = null;
    }

    private void PlayDay()
    {
        Play(dayClip);
    }

    // 밤 트랙이 없으면 낮 트랙을 그대로 유지한다(같은 클립 재요청은 매니저가 무시한다).
    private void PlayNight()
    {
        Play(nightClip != null ? nightClip : dayClip);
    }

    private void Play(AudioClip clip)
    {
        // 클립 미배선은 매니저가 조용히 무시한다 — BGM 에셋 확보 전에도 씬이 깨지지 않는다.
        AudioManager.Instance?.PlayBgm(clip, fadeSeconds);
    }
}
