using UnityEngine;

public class DayNightManagerTest : MonoBehaviour
{
    private void Start()
    {
        DayNightManager.Instance.OnDayStart += () =>
            Debug.Log($"[DayNight] 낮 시작 (Wave={DayNightManager.Instance.WaveCount})");

        DayNightManager.Instance.OnDayToNight += () =>
            Debug.Log($"[DayNight] 낮 -> 밤 (Wave={DayNightManager.Instance.WaveCount})");

        DayNightManager.Instance.OnNightToDay += () =>
            Debug.Log($"[DayNight] 밤 -> 낮 (Wave={DayNightManager.Instance.WaveCount})");
    }
}
