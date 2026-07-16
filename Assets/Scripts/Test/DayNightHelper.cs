using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(DayNightManager))]
public class DayNightHelper : MonoBehaviour
{
    private DayNightManager _dayNightManager;
    private Keyboard _keyboard;
    private DayNightManager.Phase _currentPhase;

    private void Awake()
    {
        _dayNightManager = GetComponent<DayNightManager>();
        _keyboard = Keyboard.current;
    }

    private void Update()
    {
        if (_keyboard == null)
        {
            return;
        }

        if (_keyboard.spaceKey.wasPressedThisFrame)
        {
            if (_currentPhase == DayNightManager.Phase.Day)
            {
                _dayNightManager.EndDay();
                _currentPhase = DayNightManager.Phase.Night;
            }
            else
            {
                _dayNightManager.EndNight();
                _currentPhase = DayNightManager.Phase.Day;
            }
        }
    }
}