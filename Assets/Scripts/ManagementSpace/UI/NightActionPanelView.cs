using UnityEngine;
using UnityEngine.UI;
using NorthLand.Core;

/// <summary>
/// Combat의 실제 웨이브 클리어/보스 처치 판정이 없는 상태의 임시 대체 UI(이슈 #66).
/// 좌측 하단에 낮/밤 페이즈 전환용 버튼 4개를 모아두고, 페이즈에 따라 해당 버튼만 노출한다:
/// 낮에는 "낮 종료" 하나만, 밤에는 "웨이브 성공/실패/보스 처치" 세 개만 보인다.
/// WL-018과 같은 성격의 임시 트리거 — Combat 연동 전까지만 유지한다.
/// </summary>
public class NightActionPanelView : MonoBehaviour
{
    [SerializeField] Button _endDayButton;
    [SerializeField] Button _waveSuccessButton;
    [SerializeField] Button _waveFailButton;
    [SerializeField] Button _bossKillButton;

    private ManagementController _controller;

    private void Start()
    {
        if (DayNightManager.Instance == null)
        {
            Debug.LogError("[경영] DayNightManager를 찾을 수 없습니다.");
            return;
        }

        _controller = FindFirstObjectByType<ManagementController>();

        if (_endDayButton != null)
        {
            _endDayButton.onClick.RemoveAllListeners();
            _endDayButton.onClick.AddListener(HandleEndDay);
        }
        if (_waveSuccessButton != null)
        {
            _waveSuccessButton.onClick.RemoveAllListeners();
            _waveSuccessButton.onClick.AddListener(HandleWaveSuccess);
        }
        if (_waveFailButton != null)
        {
            _waveFailButton.onClick.RemoveAllListeners();
            _waveFailButton.onClick.AddListener(HandleWaveFail);
        }
        if (_bossKillButton != null)
        {
            _bossKillButton.onClick.RemoveAllListeners();
            _bossKillButton.onClick.AddListener(HandleBossKill);
        }

        DayNightManager.Instance.OnDayToNight += ShowNightButtons;
        DayNightManager.Instance.OnNightToDay += ShowDayButton;

        ApplyPhase(DayNightManager.Instance.CurrentPhase);
    }

    private void OnDestroy()
    {
        if (DayNightManager.Instance == null)
        {
            return;
        }
        DayNightManager.Instance.OnDayToNight -= ShowNightButtons;
        DayNightManager.Instance.OnNightToDay -= ShowDayButton;
    }

    private void ShowNightButtons() => ApplyPhase(DayNightManager.Phase.Night);
    private void ShowDayButton() => ApplyPhase(DayNightManager.Phase.Day);

    private void ApplyPhase(DayNightManager.Phase phase)
    {
        bool isDay = phase == DayNightManager.Phase.Day;
        if (_endDayButton != null) _endDayButton.gameObject.SetActive(isDay);
        if (_waveSuccessButton != null) _waveSuccessButton.gameObject.SetActive(!isDay);
        if (_waveFailButton != null) _waveFailButton.gameObject.SetActive(!isDay);
        if (_bossKillButton != null) _bossKillButton.gameObject.SetActive(!isDay);
    }

    // 확인 경유·폴백 라우팅은 팝업 클래스 한 곳(Request)에 캡슐화(#219).
    private void HandleEndDay() => ManagementEndDayConfirmPopup.Request(_controller);

    private void HandleWaveSuccess()
    {
        DayNightManager.Instance.EndNight();
        Debug.Log($"[웨이브 성공] WaveCount: {DayNightManager.Instance.WaveCount}");
    }

    private void HandleWaveFail()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[웨이브 실패] GameManager가 없어 게임오버를 표시하지 못했습니다.");
            return;
        }
        GameManager.Instance.TriggerGameOver();
    }

    private void HandleBossKill()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[보스 처치] GameManager가 없어 승리 화면을 표시하지 못했습니다.");
            return;
        }
        GameManager.Instance.TriggerVictory();
    }
}
