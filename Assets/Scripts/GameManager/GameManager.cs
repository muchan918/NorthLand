using System;
using UnityEngine;
using NorthLand.UI;

namespace NorthLand.Core
{
    public enum GameResult 
    { 
        Playing, 
        GameOver, 
        Victory 
    }

    // 전투(게임) 씬의 승패 흐름을 판정·중계하는 매니저.
    // 본진 HP 0 → 게임오버 / 보스 처치 → 승리(GDD §4.4). 실제 트리거원(본진/보스)은
    // 아직 없어 지금은 외부에서 TriggerGameOver/TriggerVictory를 호출(임시). 판정 로직만
    // 담당하고, 결과 화면 조립·표시는 ResultUIManager에 위임한다(역할 분리).
    //
    // 수명주기: 결과는 Run(전투) 단위이므로 씬 스코프 싱글톤(DontDestroyOnLoad 없음) —
    // 씬을 벗어나면(메인/재시작) 새 씬에서 상태가 초기화된다. 씬 전환은 GameSceneManager 담당.
    // (DayNightManager/ResultUIManager와 동일한 씬 싱글톤 패턴 — WL-002 부채를 늘리지 않음)
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameResult Result { get; private set; } = GameResult.Playing;

        // 결과 확정 시 발생. 스폰 정지·시간 정지 등 다른 시스템이 구독해 반응할 수 있다.
        public event Action<GameResult> OnResultDecided;


        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // 패배 확정(본진 HP 0 등) 시 호출.
        public void TriggerGameOver() => Decide(GameResult.GameOver);

        // 승리 확정(보스 처치 등) 시 호출.
        public void TriggerVictory() => Decide(GameResult.Victory);

        void Decide(GameResult result)
        {
            // 이미 승패가 확정됐으면 이후 트리거는 무시(첫 판정만 유효).
            if (Result != GameResult.Playing) return;
            Result = result;

            if (ResultUIManager.Instance == null)
                Debug.LogWarning("[GameManager] ResultUIManager가 없어 결과 화면을 표시하지 못했습니다.");
            else if (result == GameResult.GameOver)
            {
                ResultUIManager.Instance.ShowGameOver();
            }
            else if (result == GameResult.Victory)
                ResultUIManager.Instance.ShowVictory();

            OnResultDecided?.Invoke(result);
        }
    }
}
