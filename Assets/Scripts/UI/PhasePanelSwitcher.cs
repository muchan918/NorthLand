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

    private void ShowNight()
    {
        // 밤 진입 시 진행 중인 입력과 선택을 정본 순서로 취소한다 — 확정 순간이 밤으로 넘어가는 것을 방지.
        // 페이즈 전환 호출부가 배치·스킬·선택 취소 순서를 직접 조합하지 않도록 MouseManager에 위임한다.
        MouseManager.Instance?.CancelInteractions();

        // 선택도 함께 해제한다(WL-086). 코디네이터가 HandleDayToNight에서 집합을 비우며 인포·사거리 원을
        // 내리지만 MouseManager의 _selected는 남아, **그 타워를 밤에 다시 클릭해도 아무것도 안 뜨고**
        // (Select의 중복 제거가 삼킨다) 초록 아웃라인만 월드에 남는다 — InteractionOutline.md §4-4의
        // "밤에도 초록·사거리 원·인포가 함께 뜬다" 불변식이 깨진다. 코디네이터와 호출 순서는 무관하다
        // (어느 쪽이 먼저 돌아도 OnDeselected는 멱등이고, 이쪽 OnPrimarySelect는 밤 게이트에 막혀 무해).
        // 반대 방향(밤→낮)은 표시와 _selected가 어긋나지 않으므로 ShowDay는 그대로 둔다.
        ApplyPhase(DayNightManager.Phase.Night);
    }

    private void ApplyPhase(DayNightManager.Phase phase)
    {
        bool isDay = phase == DayNightManager.Phase.Day;
        if (_dayPanel != null) _dayPanel.SetActive(isDay);
        if (_nightPanel != null) _nightPanel.SetActive(!isDay);
    }
}
