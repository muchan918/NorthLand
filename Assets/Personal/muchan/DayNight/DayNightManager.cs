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
    public int WaveCount { get; private set; }

    // 낮이 시작되는 모든 시점(1일차 부트스트랩 포함)에 발생
    public event Action OnDayStart;
    // 낮 -> 밤 전환 시 발생
    public event Action OnDayToNight;
    // 밤 -> 낮 전환(웨이브 종료를 의미) 시 발생
    public event Action OnNightToDay;

    private Coroutine _nightTimerRoutine;

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
        if (CurrentPhase != Phase.Day)
        {
            Debug.LogWarning("이미 밤입니다");
            return;
        }

        CurrentPhase = Phase.Night;
        OnDayToNight?.Invoke();

        _nightTimerRoutine = StartCoroutine(NightTimerRoutine());
    }

    // 임시 테스트 코드: 정식 구현에서는 웨이브 클리어 시 종료되어야 하며, UniTask로 대체 예정
    private IEnumerator NightTimerRoutine()
    {
        yield return new WaitForSeconds(3f);

        WaveCount++;
        CurrentPhase = Phase.Day;
        OnNightToDay?.Invoke();
        OnDayStart?.Invoke();
    }
}
