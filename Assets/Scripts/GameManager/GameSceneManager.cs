using UnityEngine;
using UnityEngine.SceneManagement;

namespace NorthLand.Core
{
    public class GameSceneManager : MonoBehaviour
    {
        public static GameSceneManager Instance { get; private set; }

        // 씬 이름은 Build Settings의 Scene List에 등록된 이름과 정확히 일치해야 한다.
        // 인덱스가 아니라 이름으로 로드하므로 리스트 순서가 바뀌어도 안전하다.
        // 정본 씬 이름은 항상 고정(Docs/Core/SceneWorkflow.md §1) — 씬 병합 시에도 이 상수는 바뀌지 않는다.
        const string TitleScene = "TitleScene";
        const string GameScene = "GameScene";

        private int? pendingMasterSeed;

        private bool pendingContinue;

        private RunData pendingContinueData;

        public static bool IsTitleScene => SceneManager.GetActiveScene().name == TitleScene;

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
        public void LoadMainMenu() => SceneManager.LoadScene(TitleScene);

        // 경영 공간(게임 본편)으로 전환한다. (메인 메뉴의 "게임 시작" 버튼에서 호출)
        // 랜덤 시드로 새 게임 시작
        public void LoadManageSpace()
        {
            pendingContinue = false;
            pendingContinueData = null;
            pendingMasterSeed = null;

            SceneManager.LoadScene(GameScene);
        }

        public bool TryLoadContinue(RunData data,out string error)
        {
            error = null;

            if (data == null)
            {
                error = "이어하기 RunData가 없습니다.";
                return false;
            }

            pendingContinueData = data;
            pendingContinue = true;
            pendingMasterSeed = null;

            SceneManager.LoadScene(GameScene);
            return true;
        }

        public void LoadManageSpaceWithSeed(int masterSeed)
        {
            pendingContinue = false;
            pendingContinueData = null;
            pendingMasterSeed = masterSeed;

            SceneManager.LoadScene(GameScene);
        }
        public bool TryConsumePendingMasterSeed(out int masterSeed)
        {
            if (!pendingMasterSeed.HasValue)
            {
                masterSeed = 0;
                return false;
            }

            masterSeed = pendingMasterSeed.Value;

            pendingMasterSeed = null;

            return true;
        }

        public bool TryConsumeContinueData(out RunData data)
        {
            data = null;

            if (!pendingContinue || pendingContinueData == null)
            {
                return false;
            }

            pendingContinue = false;
            data = pendingContinueData;
            pendingContinueData = null;

            return true;
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
        }
    }
}
