using UnityEngine;

/// <summary>
/// 타이틀 씬의 사운드 큐. (#361, WL-180)
///
/// **클립이 없어도 배치해야 한다.** <see cref="AudioManager"/>가 DontDestroyOnLoad라, 타이틀이
/// "무엇을 틀지"의 답을 갖지 않으면 직전 게임의 BGM(밤 트랙 포함)이 타이틀 화면에서 계속 루프한다.
/// 게임오버·클리어·복원 실패·설정 패널의 "메인으로" 등 타이틀 복귀 경로가 넷이라 어느 하나를
/// 고치는 것으로는 닫히지 않는다 — 정지 주체를 씬에 두는 쪽이 경로 수와 무관하다.
///
/// `SoundCue` 오브젝트의 자식으로 둔다(<see cref="InGameCue"/>와 같은 자리).
/// </summary>
public class TitleCue : SoundCue
{
    [SerializeField]
    [Tooltip("타이틀 트랙. 비워두면 직전 BGM을 페이드아웃하고 무음으로 둔다.")]
    private AudioClip titleClip;

    private void Start()
    {
        // 빈 클립을 PlayBgm에 넘기면 매니저가 무시해 직전 트랙이 살아남는다 — 명시적으로 끊는다.
        if (titleClip == null)
        {
            StopBgm();

            return;
        }

        PlayBgm(titleClip);
    }
}
