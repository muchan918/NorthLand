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

        // 로딩 씬. 게임 씬 진입은 전부 여기를 지난다(§Docs/Core/LoadingScene.md).
        // 이 씬이 게임 씬을 Additive로 올리고, 준비가 끝날 때까지 커튼을 유지한다.
        const string LoadingScene = "LoadingScene";

        /// <summary>로딩 흐름이 Additive로 올릴 대상 씬 이름. 씬 이름 상수의 단일 출처를 유지한다.</summary>
        public static string GameSceneName => GameScene;

        private int? pendingMasterSeed;

        // 최초 시드 시작이 튜토리얼을 거칠 때, 본 게임에 다시 전달할 시드.
        private int? tutorialReturnMasterSeed;

        private bool pendingContinue;

        private RunData pendingContinueData;

        public static bool IsTitleScene => SceneManager.GetActiveScene().name == TitleScene;

        /// <summary>
        /// 게임플레이 씬(<see cref="GameScene"/>)이 활성인가. 타이틀·로딩 중에는 거짓이다.
        ///
        /// ⚠ <b>"타이틀이 아니면 게임플레이"라고 판단하지 말 것.</b> 로딩 씬이 생기면서 그 전제가 깨졌다 —
        /// 로딩 중에는 <see cref="IsTitleScene"/>도 거짓이지만 <c>MouseManager</c> 같은 게임 씬 구성물은
        /// 아직 없다. 게임 씬 구성물의 존재를 기대하는 코드는 이 프로퍼티를 써야 한다.
        /// (로딩 중 게임 씬을 Additive로 올리는 동안에도, 활성 씬을 넘기기 전까지는 거짓이다.)
        /// </summary>
        public static bool IsGameplayScene => SceneManager.GetActiveScene().name == GameScene;

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
        public void LoadMainMenu()
        {
            TutorialMode.Exit();
            tutorialReturnMasterSeed = null;
            SceneManager.LoadScene(TitleScene);
        }

        // 경영 공간(게임 본편)으로 전환한다. (메인 메뉴의 "게임 시작" 버튼에서 호출)
        // 랜덤 시드로 새 게임 시작
        public void LoadManageSpace()
        {
            TutorialMode.Exit();
            pendingContinue = false;
            pendingContinueData = null;
            pendingMasterSeed = null;
            tutorialReturnMasterSeed = null;

            EnterGameScene();
        }

        public void LoadTutorial()
        {
            TutorialMode.Enter();
            pendingContinue = false;
            pendingMasterSeed = TutorialMode.MasterSeed;
            tutorialReturnMasterSeed = null;

            EnterGameScene();
        }

        public bool TryLoadContinue(RunData data,out string error)
        {
            error = null;

            if (data == null)
            {
                error = "이어하기 RunData가 없습니다.";
                return false;
            }

            TutorialMode.Exit();
            pendingContinueData = data;
            pendingContinue = true;
            pendingMasterSeed = null;
            tutorialReturnMasterSeed = null;

            EnterGameScene();
            return true;
        }

        public void LoadManageSpaceWithSeed(int masterSeed)
        {
            TutorialMode.Exit();
            pendingContinue = false;
            pendingContinueData = null;
            pendingMasterSeed = masterSeed;
            tutorialReturnMasterSeed = null;

            EnterGameScene();
        }

        public void LoadTutorialWithSeed(int masterSeed)
        {
            TutorialMode.Enter();
            pendingContinue = false;
            pendingMasterSeed = TutorialMode.MasterSeed;
            tutorialReturnMasterSeed = masterSeed;

            EnterGameScene();
        }

        /// <summary>
        /// 게임 씬 진입의 단일 통로. 로딩 씬을 띄우고, 게임 씬을 Additive로 올리는 일은
        /// 그쪽의 <c>LoadingFlow</c>가 맡는다.
        ///
        /// 여기서 넘기는 대기 데이터(시드·이어하기 <c>RunData</c>·튜토리얼 모드)는 전부 이 매니저가
        /// <c>DontDestroyOnLoad</c>로 들고 있으므로, 씬이 한 단계 늘어도 전달 경로는 그대로다.
        /// </summary>
        private void EnterGameScene()
        {
            // Build Settings에 로딩 씬이 없으면 LoadScene이 예외를 던진다. 등록 전이라도 게임이
            // 멈추지는 않게 옛 경로로 떨어뜨리고, 원인은 경고로 남긴다.
            if (!Application.CanStreamedLevelBeLoaded(LoadingScene))
            {
                Debug.LogWarning(
                    $"[Scene] '{LoadingScene}'이 Build Settings에 없어 로딩 화면을 건너뜁니다. " +
                    "부팅 스파이크가 그대로 노출됩니다(Docs/Core/LoadingScene.md §2).");

                SceneManager.LoadScene(GameScene);

                return;
            }

            SceneManager.LoadScene(LoadingScene);
        }

        public bool TryConsumeTutorialReturnMasterSeed(out int masterSeed)
        {
            if (!tutorialReturnMasterSeed.HasValue)
            {
                masterSeed = 0;
                return false;
            }

            masterSeed = tutorialReturnMasterSeed.Value;
            tutorialReturnMasterSeed = null;
            return true;
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
