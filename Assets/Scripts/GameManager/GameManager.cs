using System;
using NorthLand.UI;
using UnityEngine;

namespace NorthLand.Core
{
    public enum GameResult
    {
        Playing,
        GameOver,
        Victory
    }

    /// <summary>
    /// 전투 씬의 승패 상태를 판정하고 결과를 전달하는 매니저.
    /// 본진 HP가 0이 되면 게임오버, 보스를 처치하면 승리로 확정한다.
    /// 결과 화면 표시는 ResultUIManager에 맡기고,
    /// 시간 정지 등의 후속 처리는 OnResultDecided 이벤트로 전달한다.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameResult Result { get; private set; }
            = GameResult.Playing;

        /// <summary>
        /// 게임 결과가 최초로 확정됐을 때 발생한다.
        /// GameSpeedController, SkillManager 등의 시스템이 구독한다.
        /// </summary>
        public event Action<GameResult> OnResultDecided;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 본진 HP가 0이 되는 등 패배 조건이 발생했을 때 호출한다.
        /// </summary>
        public void TriggerGameOver()
        {
            Decide(GameResult.GameOver);
        }

        /// <summary>
        /// 최종 보스를 처치하는 등 승리 조건이 발생했을 때 호출한다.
        /// </summary>
        public void TriggerVictory()
        {
            Decide(GameResult.Victory);
        }

        private void Decide(GameResult result)
        {
            // 최초 결과만 인정한다.
            if (Result != GameResult.Playing)
            {
                return;
            }

            // Playing은 종료 결과로 사용할 수 없다.
            if (result == GameResult.Playing)
            {
                Debug.LogWarning("[GameManager] Playing 상태로 결과를 확정할 수 없습니다.",this);

                return;
            }

            Result = result;

            ShowResultUI(result);

            // UI 존재 여부와 무관하게 결과 이벤트를 발생시킨다.
            // GameSpeedController가 이 이벤트를 받아 시간을 정지한다.
            OnResultDecided?.Invoke(result);
        }

        private void ShowResultUI(GameResult result)
        {
            if (ResultUIManager.Instance == null)
            {
                Debug.LogWarning("[GameManager] ResultUIManager가 없어 " +"결과 화면을 표시하지 못했습니다.",this);

                return;
            }

            switch (result)
            {
                case GameResult.GameOver:
                    ResultUIManager.Instance.ShowGameOver();
                    break;

                case GameResult.Victory:
                    ResultUIManager.Instance.ShowVictory();
                    break;
            }
        }
    }
}