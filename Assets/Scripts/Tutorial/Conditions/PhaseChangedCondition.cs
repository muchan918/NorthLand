using System;
using UnityEngine;

// 낮/밤 전환이 일어나면 충족된다.
[Serializable]
public class PhaseChangedCondition : TutorialCondition
{
    [SerializeField]
    private DayNightManager.Phase target = DayNightManager.Phase.Night;

    private DayNightManager _dayNight;

    public override void Begin(TutorialContext context)
    {
        _dayNight = context.DayNight;

        if (_dayNight == null)
        {
            Debug.LogWarning("[Tutorial] DayNightManager가 없다 — 이 단계는 넘어가지 않는다.");
            return;
        }

        if (target == DayNightManager.Phase.Night)
        {
            _dayNight.OnDayToNight += OnReached;
        }
        else
        {
            _dayNight.OnNightToDay += OnReached;
        }
    }

    public override void End()
    {
        if (_dayNight == null)
        {
            return;
        }

        // 어느 쪽을 걸었든 둘 다 푼다 — 안 걸린 쪽을 빼는 건 무해하다.
        _dayNight.OnDayToNight -= OnReached;
        _dayNight.OnNightToDay -= OnReached;
        _dayNight = null;
    }

    private void OnReached()
    {
        Fire();
    }
}