using UnityEngine;

/// <summary>
/// 이 씬의 페이즈 오디오 큐 — 어떤 BGM을 틀지와 낮/밤 전환 스팅어를 정한다. (#361)
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

    [Header("Phase Stingers")]
    [SerializeField]
    [Tooltip("낮→밤 전환 순간에 1회 재생할 효과음. SFX 채널 볼륨을 따른다.")]
    private AudioClip dayToNightClip;

    [SerializeField]
    [Tooltip("밤→낮 전환 순간에 1회 재생할 효과음. SFX 채널 볼륨을 따른다.")]
    private AudioClip nightToDayClip;

    // 임포트 설정에는 클립별 게인이 없다 — 에셋을 다시 내보내지 않고 소리를 줄이려면 재생 배율뿐이다.
    // 현재 두 스팅어는 피크가 -0.8/-1.0 dBFS로 꽉 차 있어 SFX 볼륨 그대로면 BGM 위에서 과하게 들린다.
    // 기본 0.35 ≈ -9dB. 두 클립의 레벨 차가 0.4dB뿐이라 공용 배율 하나로 충분하다.
    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("전환 스팅어 재생 배율. SFX 채널 볼륨에 곱해진다.")]
    private float stingerVolume = 0.35f;

    // 구독한 대상을 그대로 들고 있다가 해제한다. 씬 파괴 순서에 따라
    // DayNightManager.Instance가 이미 갈아치워졌거나 null일 수 있다.
    private DayNightManager subscribed;

    private void Start()
    {
        DayNightManager dayNight = DayNightManager.Instance;

        // 밤 트랙이 없어도 전환 스팅어만 쓸 수 있으므로 페이즈가 있는 씬이면 항상 구독한다.
        if (dayNight != null)
        {
            subscribed = dayNight;

            subscribed.OnDayToNight += HandleDayToNight;
            subscribed.OnNightToDay += HandleNightToDay;
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

        subscribed.OnDayToNight -= HandleDayToNight;
        subscribed.OnNightToDay -= HandleNightToDay;

        subscribed = null;
    }

    // 전환 스팅어는 **전환 순간에만** 울린다 — Start의 초기 1회는 PlayDay/PlayNight를 직접 부른다.
    private void HandleDayToNight()
    {
        PlayStinger(dayToNightClip);

        PlayNight();
    }

    private void HandleNightToDay()
    {
        PlayStinger(nightToDayClip);

        PlayDay();
    }

    private void PlayStinger(AudioClip clip)
    {
        // `?.`는 Unity의 == 오버로드를 우회해 파괴된 객체를 살아 있는 것처럼 다룬다(UNT0008).
        if (AudioManager.Instance == null)
        {
            return;
        }

        AudioManager.Instance.PlaySfx(clip, stingerVolume);
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
        if (AudioManager.Instance == null)
        {
            return;
        }

        // 클립 미배선은 매니저가 조용히 무시한다 — BGM 에셋 확보 전에도 씬이 깨지지 않는다.
        AudioManager.Instance.PlayBgm(clip, fadeSeconds);
    }
}
