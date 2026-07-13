using UnityEngine;
using NorthLand.Core;

namespace NorthLand.UI
{
    // 게임 씬의 결과 UI(게임오버/승리 패널)를 켜고 끄는 표시 전용 매니저.
    // "언제 게임오버/승리인지" 판단하는 로직은 여기 두지 않는다 — 외부(게임 플로우/
    // 임시 트리거)가 Show~()를 호출하면 해당 패널만 활성화한다.
    //
    // 수명주기: 결과 UI는 게임 씬에 종속되므로 씬 스코프 싱글톤으로 둔다
    // (DontDestroyOnLoad 없음 — 씬을 넘어 살아남을 이유가 없다). 씬 전환은 GameSceneManager 담당.
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [SerializeField] GameObject gameOverPanel;
        [SerializeField] GameObject victoryPanel;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 시작 시 결과 패널은 모두 숨긴다(플레이 중에만 조건부로 노출).
            HideAll();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // 게임오버 결과 패널을 띄운다. (본진 HP 0 등 패배 확정 시 외부에서 호출)
        public void ShowGameOver()
        {
            HideAll();
            SetActiveSafe(gameOverPanel, true);
        }

        // 승리 결과 패널을 띄운다. (보스 처치 등 클리어 확정 시 외부에서 호출)
        public void ShowVictory()
        {
            HideAll();
            SetActiveSafe(victoryPanel, true);
        }

        // 모든 결과 패널을 숨긴다.
        public void HideAll()
        {
            SetActiveSafe(gameOverPanel, false);
            SetActiveSafe(victoryPanel, false);
        }

        // --- 결과 UI 네비게이션 버튼 핸들러 (씬 전환은 GameSceneManager에 위임) ---
        // 버튼 onClick에서 GameSceneManager를 직접 참조할 수 없어(런타임 생성 싱글톤)
        // 씬에 존재하는 이 컴포넌트를 경유한다. 판정 로직이 아니라 화면 이동만 담당.

        // "메인으로" 버튼: 메인 메뉴로 전환.
        public void OnClickMainMenu()
        {
            if (GameSceneManager.Instance == null)
            {
                Debug.LogError("[UIManager] GameSceneManager 인스턴스가 없습니다. 씬 전환 불가.");
                return;
            }
            GameSceneManager.Instance.LoadMainMenu();
        }

        // "다시하기" 버튼: 게임(경영) 씬을 다시 로드해 Run을 재시작.
        public void OnClickRetry()
        {
            if (GameSceneManager.Instance == null)
            {
                Debug.LogError("[UIManager] GameSceneManager 인스턴스가 없습니다. 씬 전환 불가.");
                return;
            }
            GameSceneManager.Instance.LoadManageSpace();
        }

        // 참조 미할당 시 NullRef 대신 경고만 남기고 넘어간다(씬 배선 누락 방어).
        void SetActiveSafe(GameObject panel, bool active)
        {
            if (panel == null)
            {
                Debug.LogWarning("[UIManager] 결과 패널 참조가 비어 있습니다. 인스펙터 배선을 확인하세요.");
                return;
            }
            panel.SetActive(active);
        }
    }
}
