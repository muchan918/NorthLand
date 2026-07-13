using UnityEngine;
using UnityEngine.SceneManagement;

namespace NorthLand.Core
{
    public class GameSceneManager : MonoBehaviour
    {
        public static GameSceneManager Instance { get; private set; }

        // 씬 이름은 Build Settings의 Scene List에 등록된 이름과 정확히 일치해야 한다.
        // 인덱스가 아니라 이름으로 로드하므로 리스트 순서가 바뀌어도 안전하다.
        const string MainMenuScene = "MainMenu";
        const string ManageSpaceScene = "ManageSpace-Sungsoo"; //TODO: Integration 테스트로 이관시 실제 게임 씬 명으로 변경 할 예정

        // 첫 씬이 로드되기 전에 Unity가 자동 호출한다. 매니저를 여기서 부팅한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;

            var go = new GameObject(nameof(GameSceneManager));
            Instance = go.AddComponent<GameSceneManager>();
            DontDestroyOnLoad(go);
        }

        // 혹시 씬에 수동 배치되는 등으로 중복 생성될 경우를 대비한 방어 코드.
        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // 메인 메뉴로 전환한다. (게임 클리어/오버 패널의 "메인으로" 버튼 등에서 호출)
        public void LoadMainMenu() => SceneManager.LoadScene(MainMenuScene);

        // 경영 공간(게임 본편)으로 전환한다. (메인 메뉴의 "게임 시작" 버튼에서 호출)
        public void LoadManageSpace() => SceneManager.LoadScene(ManageSpaceScene);
    }
}
