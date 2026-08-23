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

    // 건물 사건(업그레이드·주민 증축) 통지의 발신자. 위와 같은 이유로 구독한 인스턴스를 붙잡아 둔다.
    private ManagementController subscribedManagement;

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

        // 건물 사건은 **컨트롤러의 통지**로 받는다(WL-208). 예전에는 패널 버튼 핸들러에서 직접 소리를
        // 냈는데, 같은 사건의 파티클은 이미 이 이벤트를 구독하고 있어(`BuildingFeedback`) 트리거가 두 벌로
        // 갈려 있었다 — 진입점이 늘면 파티클만 따라오고 소리는 조용히 빠진다(실제로 `BuildingsUpgradeHelper`가
        // 그런 경로였다). 이 씬 큐가 "언제 무엇을 틀지"를 정하는 자리이므로 구독도 여기에 둔다.
        //
        // 씬 로드는 모든 오브젝트를 만든 뒤 Start를 돌리므로 여기서 탐색해도 안전하다(`BuildingFeedback`과 같은 근거).
        // 경영이 없는 씬(타이틀·전투 테스트)에서는 조용히 건너뛴다.
        ManagementController management = FindFirstObjectByType<ManagementController>();

        if (management != null)
        {
            subscribedManagement = management;

            subscribedManagement.OnBuildingAction += HandleBuildingAction;
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
        if (subscribedManagement != null)
        {
            subscribedManagement.OnBuildingAction -= HandleBuildingAction;

            subscribedManagement = null;
        }

        if (subscribed == null)
        {
            return;
        }

        subscribed.OnDayToNight -= HandleDayToNight;
        subscribed.OnNightToDay -= HandleNightToDay;

        subscribed = null;
    }

    /// <summary>
    /// 건물에 플레이어 행동이 반영됐을 때의 **성공음**. 실패(자원 부족 등)는 이 이벤트로 오지 않으므로
    /// 거절음은 여전히 버튼 핸들러가 낸다 — 컨트롤러는 성공만 알린다.
    ///
    /// 아직 소리가 없는 행동(주민 배치·해제)은 그냥 지나간다. 소리를 늘릴 땐 여기 분기만 추가하면 되고
    /// 호출부는 건드리지 않는다 — <see cref="BuildingFeedback"/>가 파티클을 늘리는 방식과 같다.
    /// </summary>
    private void HandleBuildingAction(BuildingAsset building, ManagementController.BuildingAction action)
    {
        switch (action)
        {
            case ManagementController.BuildingAction.Upgraded:
                Sfx.BuildingUpgraded();
                break;

            case ManagementController.BuildingAction.VillagerIncreased:
                Sfx.ResidentIncreased();
                break;
        }
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
