using UnityEngine;

public class DayNightManagerTest : MonoBehaviour
{
    private void Start()
    {
        if (DayNightManager.Instance == null)
        {
            Debug.LogError("DayNightManager 없음");
            return;
        }

        DayNightManager.Instance.OnDayStart += LogDayStart;
        DayNightManager.Instance.OnDayToNight += LogDayToNight;
        DayNightManager.Instance.OnNightToDay += LogNightToDay;
    }

    private void OnDestroy()
    {
        if (DayNightManager.Instance == null) return;
        DayNightManager.Instance.OnDayStart -= LogDayStart;
        DayNightManager.Instance.OnDayToNight -= LogDayToNight;
        DayNightManager.Instance.OnNightToDay -= LogNightToDay;
    }

    private void LogDayStart() =>
        Debug.Log($"[DayNight] 낮 시작 (Wave={DayNightManager.Instance.WaveCount})");

    private void LogDayToNight() =>
        Debug.Log($"[DayNight] 낮 -> 밤 (Wave={DayNightManager.Instance.WaveCount})");

    private void LogNightToDay() =>
        Debug.Log($"[DayNight] 밤 -> 낮 (Wave={DayNightManager.Instance.WaveCount})");
}
