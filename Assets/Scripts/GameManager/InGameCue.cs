using UnityEngine;

/// <summary>
/// 게임 씬의 사운드 큐 — 낮/밤 BGM 교체와 페이즈 전환 스팅어를 정한다. (#361)
///
/// 낮/밤 구독이 씬 쪽에 있으므로 <see cref="AudioManager"/>(DontDestroyOnLoad)가 씬마다 죽는
/// <see cref="DayNightManager"/>를 재구독할 일이 없다.
///
/// 씬당 하나 배치한다 — `SoundCue` 오브젝트의 자식으로 둔다(<see cref="TitleCue"/>와 같은 자리).
/// </summary>
public class InGameCue : SoundCue
{
    [Header("Tracks")]
    [SerializeField]
    [Tooltip("낮 트랙.")]
    private AudioClip dayClip;

    [SerializeField]
    [Tooltip("밤 트랙. 비워두면 밤에도 낮 트랙을 유지한다.")]
    private AudioClip nightClip;

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

        // 초기 1회. ⚠ 세이브 복원(RunSaveManager도 Start에서 돈다)과 순서가 보장되지 않는다 —
        // 지금은 v1이 밤 페이즈 복원을 거부해서 드러나지 않지만, 밤 세이브를 여는 순간
        // "밤에서 이어했는데 낮 BGM"이 확률적으로 난다(Docs/Core/AudioManager.md §5).
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
        PlaySfx(dayToNightClip, stingerVolume);

        PlayNight();
    }

    private void HandleNightToDay()
    {
        PlaySfx(nightToDayClip, stingerVolume);

        PlayDay();
    }

    private void PlayDay()
    {
        PlayBgm(dayClip);
    }

    // 밤 트랙이 없으면 낮 트랙을 그대로 유지한다(같은 클립 재요청은 매니저가 무시한다).
    private void PlayNight()
    {
        PlayBgm(nightClip != null ? nightClip : dayClip);
    }
}
