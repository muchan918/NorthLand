using System;
using System.Collections;
using UnityEngine;

public class DayNightManager : MonoBehaviour
{
    public enum Phase
    {
        Day,
        Night
    }

    public static DayNightManager Instance { get; private set; }

    public Phase CurrentPhase { get; private set; }

    // 지금까지 클리어한 웨이브 수. 첫 낮에는 0이다(아직 아무것도 클리어하지 않았으므로).
    public int WaveCount { get; private set; }

    // 지금 준비 중이거나 진행 중인 웨이브 번호(1부터). 첫 낮 = 1.
    // 표시(UI)와 스폰 라운드 번호는 WaveCount가 아니라 항상 이 값을 쓴다.
    public int CurrentWave => WaveCount + 1;

    /// <summary>
    /// 웨이브 게이트(타워 해금 등)가 쓰는 정적 조회창. 매니저가 없는 씬(타워 테스트 씬 등)은
    /// <see cref="int.MaxValue"/>를 내 **"웨이브 제한 없음"**으로 본다 — permissive 규약의 단일 출처다.
    /// <br/>
    /// 이 규약을 여기 두는 이유: 게이트를 판정하는 쪽(<see cref="TowerAsset.IsUnlocked"/>)은 데이터라
    /// 씬을 알 필요가 없고, 규약을 호출부마다 적으면 화면별로 갈린다. <see cref="Instance"/>만 보므로
    /// 비활성 매니저는 없는 것으로 취급되는데, 그런 오브젝트는 <see cref="OnDayStart"/>도 발행하지 않아
    /// 어차피 웨이브가 오르지 않는다(게이트만 잠그면 영구 잠금이 된다).
    /// </summary>
    public static int CurrentWaveOrMax => Instance != null ? Instance.CurrentWave : int.MaxValue;

    // 낮이 시작되는 모든 시점(1일차 부트스트랩 포함)에 발생
    public event Action OnDayStart;
    // 낮 -> 밤 전환 시 발생
    public event Action OnDayToNight;
    // 밤 -> 낮 전환(웨이브 종료를 의미) 시 발생
    public event Action OnNightToDay;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        CurrentPhase = Phase.Day;
    }

    private void Start()
    {
        // 다른 오브젝트의 Start()에서 이벤트를 구독할 시간을 주기 위해 한 프레임 지연 후 발생시킴
        StartCoroutine(FireInitialDayStart());
    }

    private IEnumerator FireInitialDayStart()
    {
        yield return null;
        OnDayStart?.Invoke();
    }

    // ── 외부 진입점 ────────────────────────────────────────────────
    public void EndDay()
    {
        // UI 버튼이나 확인 팝업이 이미 열린 뒤 튜토리얼 조건이 바뀌어도,
        // 실제 페이즈 전환 진입점에서 다시 검사해 우회 진입을 막는다.
        if (!TutorialInputGate.AllowsEndDay())
        {
            return;
        }

        if (CurrentPhase != Phase.Day)
        {
            Debug.LogWarning("이미 밤입니다");
            return;
        }

        CurrentPhase = Phase.Night;
        OnDayToNight?.Invoke();

        // 임시 3초 자동 타이머 테스트는 비활성화 — EndNight()을 버튼으로 직접 호출해서 테스트
        // _nightTimerRoutine = StartCoroutine(NightTimerRoutine());
    }

    public void EndNight()
    {
        if (CurrentPhase != Phase.Night)
        {
            Debug.LogWarning("이미 낮입니다");
            return;
        }

        WaveCount++;
        CurrentPhase = Phase.Day;
        OnNightToDay?.Invoke();
        OnDayStart?.Invoke();
    }

    // 임시 테스트 코드: 정식 구현에서는 웨이브 클리어 시 EndNight()를 호출해야 하며, UniTask로 대체 예정
    // private IEnumerator NightTimerRoutine()
    // {
    //     yield return new WaitForSeconds(3f);
    //     EndNight();
    // }

    // [테스트 훅] 주민 배치·영토 확장·몬스터 스폰 없이 웨이브 수만 올리고 낮 상태를 유지한 채 다음 날로 넘어간다.
    // 정상 절차(EndDay→몬스터 스폰→WaveCleared→EndNight)를 전부 건너뛰므로 OnDayToNight/OnNightToDay는 발행하지 않는다.
    public void SkipDay()
    {
        if (CurrentPhase != Phase.Day)
        {
            Debug.LogWarning("밤에는 SkipDay를 사용할 수 없습니다");
            return;
        }

        WaveCount++;
        OnDayStart?.Invoke();
    }

    /// <summary>
    /// 저장된 웨이브 수와 페이즈를 절대값으로 복원한다.
    /// 복원 중 자동 저장과 게임 진행 이벤트가 발생하지 않도록
    /// 페이즈 전환 이벤트는 발행하지 않는다.
    /// </summary>
    public bool TryRestoreState(int waveCount,Phase phase)
    {
        if (waveCount < 0)
        {
            Debug.LogError($"[DayNight] WaveCount는 음수일 수 없습니다: {waveCount}",this);

            return false;
        }

        if (!Enum.IsDefined(typeof(Phase),phase))
        {
            Debug.LogError($"[DayNight] 알 수 없는 페이즈입니다: {(int)phase}",this);

            return false;
        }

        WaveCount = waveCount;
        CurrentPhase = phase;

        return true;
    }
}
