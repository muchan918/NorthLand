using UnityEngine;

/// <summary>
/// 페이즈별 하단 액션 패널 교체(#134). 스킬은 밤 전용이므로, 낮에는 타워 배치 패널을,
/// 밤에는 같은 자리에 스킬 패널을 노출한다. <see cref="DayNightManager"/>의 전환 이벤트를 구독해
/// 두 패널의 활성 상태를 토글한다(페이즈 반응 시스템은 이벤트 훅 구조 — 팀 계약 #5).
/// 밤→낮 전환 시 진행 중이던 스킬 조준을 취소한다. <see cref="NightActionPanelView"/>와 동일한 패턴.
/// </summary>
public class PhasePanelSwitcher : MonoBehaviour
{
    [SerializeField] GameObject _dayPanel;   // 낮: 타워 배치 패널(TowerPanel)
    [SerializeField] GameObject _nightPanel; // 밤: 스킬 패널(SkillPanel)

    private void Start()
    {
        if (DayNightManager.Instance == null)
        {
            Debug.LogError("[페이즈패널] DayNightManager를 찾을 수 없습니다.");
            return;
        }

        // OnDayStart는 부트스트랩(1일차 포함) 매 낮 시작에 발생 → 낮 패널 신호로 사용.
        DayNightManager.Instance.OnDayStart += ShowDay;
        DayNightManager.Instance.OnDayToNight += ShowNight;

        ApplyPhase(DayNightManager.Instance.CurrentPhase);
    }

    private void OnDestroy()
    {
        if (DayNightManager.Instance == null) return;
        DayNightManager.Instance.OnDayStart -= ShowDay;
        DayNightManager.Instance.OnDayToNight -= ShowNight;
    }

    private void ShowDay()
    {
        // 밤→낮 전환 시 진행 중인 스킬 조준을 취소(부트스트랩에서 호출돼도 무해).
        MouseManager.Instance?.CancelSkillTargeting();
        ApplyPhase(DayNightManager.Phase.Day);
    }

    private void ShowNight() => ApplyPhase(DayNightManager.Phase.Night);

    private void ApplyPhase(DayNightManager.Phase phase)
    {
        bool isDay = phase == DayNightManager.Phase.Day;
        if (_dayPanel != null) _dayPanel.SetActive(isDay);
        if (_nightPanel != null) _nightPanel.SetActive(!isDay);
    }
}
