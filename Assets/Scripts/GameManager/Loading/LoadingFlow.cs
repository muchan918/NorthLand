using System;
using System.Threading;
using CombatSpace;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace NorthLand.Core
{
    /// <summary>
    /// 로딩 씬이 소유하는 부팅 오케스트레이터. 워밍업 → 게임 씬 Additive 적재 → 준비 완료 대기 →
    /// 활성 씬 전환 → 커튼 걷기 순서를 여기 한 곳이 가진다.
    ///
    /// <b>왜 Additive인가.</b> 게임 씬을 <c>Single</c>로 로드하면 활성화되는 순간 이 커튼이 함께
    /// 파괴되어, 부팅 프레임(실측 976.77ms — <c>Docs/Core/LoadingScene.md</c> §2)이 그대로 노출된다.
    /// Additive로 올리면 게임 씬의 <c>Awake</c>/<c>Start</c>가 다 도는 동안에도 이 씬이 살아 있어
    /// 커튼이 유지된다.
    ///
    /// <b>게임 씬 카메라를 끄지 않는다.</b> 커튼은 Screen Space - Overlay 캔버스가 덮는 것으로 만든다.
    /// 카메라를 끄면 URP RenderGraph 최초 컴파일과 렌더 경로 JIT(실측 444.75ms — 같은 문서 §2.1)가
    /// 커튼 뒤에서 일어나지 못하고, 커튼을 걷은 직후에 그대로 터진다.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class LoadingFlow : MonoBehaviour
    {
        [SerializeField]
        private LoadingScreen screen;

        [Header("Timing")]
        [Tooltip("커튼을 최소 이만큼은 보여 준다. 로딩이 빨라도 한 프레임 번쩍이지 않게 한다.")]
        [SerializeField]
        [Min(0f)]
        private float minimumDisplaySeconds = 0.6f;

        [Tooltip("전투맵 초기화 완료를 기다리는 상한(초). 넘으면 경고를 남기고 커튼을 걷는다.")]
        [SerializeField]
        [Min(1f)]
        private float readyTimeoutSeconds = 30f;

        private CancellationTokenSource lifetimeCts;

        private void Start()
        {
            lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());

            RunAsync(lifetimeCts.Token).Forget();
        }

        private void OnDestroy()
        {
            lifetimeCts?.Cancel();
            lifetimeCts?.Dispose();
            lifetimeCts = null;
        }

        private async UniTaskVoid RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                await LoadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 씬이 먼저 내려간 정상 경로다. 조용히 끝낸다.
            }
            catch (Exception exception)
            {
                // 여기서 죽으면 커튼이 영원히 덮인 채 남는다 — 최소한 원인을 남긴다.
                Debug.LogException(exception, this);
            }
        }

        private async UniTask LoadAsync(CancellationToken cancellationToken)
        {
            float startTime = Time.unscaledTime;

            // ── 0.00~0.10 로컬라이제이션 ────────────────────────────────────────────────
            Report(0f);
            await BootWarmup.WarmLocalizationAsync(cancellationToken);
            Report(0.10f);

            // ── 0.10~0.30 데이터 테이블 · 타워 에셋(최대 항목) ─────────────────────────
            BootWarmup.WarmDataTables();

            // 한 프레임 넘겨 준다. 아래 타워 적재가 콜드에서 수백 ms라 같은 프레임에 붙이면
            // 로딩 애니메이션이 두 배로 오래 멈춘 것처럼 보인다.
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

            BootWarmup.WarmTowerAssets();
            Report(0.30f);

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

            BootWarmup.WarmSharedVisuals();
            Report(0.40f);

            // ── 0.40~0.65 게임 씬 Additive 적재 ────────────────────────────────────────
            // 게임 씬에도 AudioListener와 EventSystem이 있다. 두 개가 공존하면 Unity가 경고를 뱉고
            // 입력이 어느 쪽으로 갈지 모호해지므로, 올리기 **전에** 이쪽을 접는다.
            DisableLocalSingletonComponents();

            AsyncOperation load = SceneManager.LoadSceneAsync(
                GameSceneManager.GameSceneName,
                LoadSceneMode.Additive);

            if (load == null)
            {
                Debug.LogError(
                    $"[Loading] '{GameSceneManager.GameSceneName}' 씬을 로드할 수 없습니다. " +
                    "Build Settings의 Scene List 등록을 확인하세요.",
                    this);

                return;
            }

            while (!load.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Report(Mathf.Lerp(0.40f, 0.65f, load.progress));

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            Report(0.65f);

            // ── 0.65~0.95 준비 완료 대기 ───────────────────────────────────────────────
            // Additive 로드가 끝난 시점에 게임 씬의 Awake는 이미 다 돌았지만 Start는 다음 프레임이다.
            // 전투맵 초기화가 그 Start에서 일어나므로 여기서 완료를 확인해야 한다.
            await WaitForCombatMapAsync(cancellationToken);
            Report(0.95f);

            // ── 0.95~1.00 활성 씬 전환 · 커튼 걷기 ────────────────────────────────────
            Scene gameScene = SceneManager.GetSceneByName(GameSceneManager.GameSceneName);

            if (gameScene.IsValid())
            {
                // 활성 씬은 라이팅·스카이박스의 주인이고 GameSceneManager의 씬 판정 기준이기도 하다.
                // 언로드보다 먼저 옮겨야 한다 — 활성 씬은 언로드할 수 없다.
                SceneManager.SetActiveScene(gameScene);
            }

            float elapsed = Time.unscaledTime - startTime;

            if (elapsed < minimumDisplaySeconds)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(minimumDisplaySeconds - elapsed),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    cancellationToken);
            }

            Report(1f);

            if (screen != null)
            {
                screen.SnapProgress();
                await screen.FadeOutAsync(cancellationToken);
            }

            // 이 씬을 내리면 이 컴포넌트도 함께 파괴된다 — 이후에 아무것도 두지 않는다.
            await SceneManager.UnloadSceneAsync(gameObject.scene)
                .ToUniTask(cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 전투맵 초기화 완료를 기다린다. 판정은 이미 공개된 <see cref="CombatMapInitializer.IsInitialized"/>를
        /// 쓴다 — 그래서 이 단계는 CombatSpace 쪽 코드를 고치지 않고도 성립한다.
        ///
        /// 초기화 담당이 아예 없는 씬(주민 테스트 씬 등)도 있으므로, 없으면 기다리지 않고 넘어간다.
        /// </summary>
        private async UniTask WaitForCombatMapAsync(CancellationToken cancellationToken)
        {
            CombatMapInitializer initializer =
                FindFirstObjectByType<CombatMapInitializer>(FindObjectsInactive.Include);

            if (initializer == null)
            {
                Debug.LogWarning(
                    "[Loading] CombatMapInitializer를 찾지 못해 전투맵 준비를 기다리지 않습니다. " +
                    "전투 공간이 없는 씬이면 정상입니다.",
                    this);

                return;
            }

            float deadline = Time.unscaledTime + readyTimeoutSeconds;

            while (!initializer.IsInitialized)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Time.unscaledTime > deadline)
                {
                    // ⚠ 여기서 그냥 계속 기다리면 커튼이 영원히 덮인다. 덜 된 화면을 보여 주더라도
                    //   플레이어를 가둬 두는 것보다 낫고, 콘솔에 원인이 남는다.
                    Debug.LogError(
                        $"[Loading] 전투맵 초기화가 {readyTimeoutSeconds:0}초 안에 끝나지 않아 " +
                        "대기를 중단하고 커튼을 걷습니다.",
                        this);

                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        /// <summary>
        /// 게임 씬과 겹치면 곤란한 이 씬의 단독 컴포넌트를 접는다.
        /// 카메라는 <b>끄지 않는다</b> — 클래스 주석의 렌더 워밍 근거를 참고할 것.
        /// </summary>
        private void DisableLocalSingletonComponents()
        {
            Scene here = gameObject.scene;

            foreach (GameObject root in here.GetRootGameObjects())
            {
                foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
                {
                    listener.enabled = false;
                }

                foreach (EventSystem eventSystem in root.GetComponentsInChildren<EventSystem>(true))
                {
                    eventSystem.enabled = false;
                }
            }
        }

        private void Report(float value)
        {
            if (screen != null) screen.ReportProgress(value);
        }
    }
}
