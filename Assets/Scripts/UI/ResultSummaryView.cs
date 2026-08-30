using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NorthLand.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NorthLand.UI
{
    /// 결과창에 이번 판의 요약 정보(현재 시드·도달 웨이브)를 채우고, 시드를 클립보드로 복사한다.
    ///
    /// **값을 스스로 갱신하지 않는다.** 결과창은 판이 끝난 시점의 스냅샷이라 이후 값이 변할 일이
    /// 없고, 매 프레임 폴링하면 `Time.timeScale`이 0인 화면에서 의미 없는 일을 계속하게 된다.
    /// <see cref="ResultPanelAnimator"/>가 패널을 띄우면서 <see cref="Bind"/>를 한 번 부른다.
    ///
    /// 소스가 없어도 결과창 자체는 반드시 떠야 하므로(여기서 예외가 나면 플레이어가 화면에 갇힌다)
    /// 모든 조회 실패는 <see cref="Unavailable"/> 표기로 떨어뜨린다.
    public class ResultSummaryView : MonoBehaviour
    {
        /// 값을 못 읽었을 때의 표기. 빈 문자열로 두면 라벨만 남아 "버그로 지워진 것"처럼 보인다.
        private const string Unavailable = "-";

        [Header("표시 대상")]
        [SerializeField]
        [Tooltip("현재 시드 숫자가 들어갈 텍스트.")]
        private TMP_Text seedValue;

        [SerializeField]
        [Tooltip("도달 웨이브 숫자가 들어갈 텍스트.")]
        private TMP_Text waveValue;

        [Header("시드 복사")]
        [SerializeField]
        [Tooltip("시드를 클립보드로 복사하는 버튼. 시드를 못 읽으면 자동으로 비활성화된다.")]
        private Button copyButton;

        [SerializeField]
        [Tooltip("복사 직후 잠깐 떠오르는 \"복사됨\" 표기.")]
        private CanvasGroup copiedFeedback;

        [SerializeField]
        [Tooltip("\"복사됨\" 표기가 떠 있는 시간(초).")]
        private float copiedFeedbackDuration = 1.1f;

        [Header("소스")]
        [SerializeField]
        [Tooltip("마스터 시드를 제공한다. 비우면 씬에서 찾는다.")]
        private RunBootstrapper runBootstrapper;

        /// 마지막 <see cref="Bind"/>에서 확정한 시드. 복사와 (후속) 같은 시드 재시작이 공유하는 단일 값이다 —
        /// 복사 시점에 다시 조회하면 화면에 보이는 숫자와 클립보드가 갈릴 수 있다.
        private int? boundSeed;

        private CancellationTokenSource feedbackCts;

        /// 화면에 표시 중인 마스터 시드. 확정되지 않았으면 false.
        public bool TryGetSeed(out int seed)
        {
            seed = boundSeed ?? 0;

            return boundSeed.HasValue;
        }

        /// 지금 화면에 표시할 값을 한 번 읽어 채운다.
        public void Bind()
        {
            boundSeed = ResolveSeed();

            SetText(seedValue, boundSeed.HasValue ? boundSeed.Value.ToString() : Unavailable);
            SetText(waveValue, ResolveWave());

            // 복사할 값이 없으면 버튼을 잠근다. 눌리는데 아무 일도 안 일어나는 버튼이
            // "복사가 안 된다"로 읽히는 것을 막는다.
            if (copyButton != null)
            {
                copyButton.interactable = boundSeed.HasValue;
            }

            SetFeedbackAlpha(0f);
        }

        /// 시드를 클립보드에 넣는다. 복사 버튼의 onClick에서 부른다.
        public void CopySeedToClipboard()
        {
            if (!boundSeed.HasValue)
            {
                return;
            }

            GUIUtility.systemCopyBuffer = boundSeed.Value.ToString();

            ShowCopiedFeedback();
        }

        private void OnDisable()
        {
            // UniTask는 GameObject가 꺼져도 계속 돈다. 여기서 끊지 않으면 꺼진 패널의
            // 피드백이 배후에서 이어지고 다음 표시 때 "복사됨"이 한 프레임 비친다.
            CancelFeedback();
            SetFeedbackAlpha(0f);
        }

        private void OnDestroy()
        {
            CancelFeedback();
        }

        private void ShowCopiedFeedback()
        {
            if (copiedFeedback == null)
            {
                return;
            }

            CancelFeedback();

            feedbackCts = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());

            FeedbackAsync(feedbackCts.Token).Forget();
        }

        private void CancelFeedback()
        {
            if (feedbackCts == null)
            {
                return;
            }

            feedbackCts.Cancel();
            feedbackCts.Dispose();
            feedbackCts = null;
        }

        /// ⚠ 시간축은 unscaled다. 결과창은 `timeScale`이 0인 화면이라
        /// scaled로 두면 "복사됨"이 뜬 채로 영영 사라지지 않는다.
        private async UniTaskVoid FeedbackAsync(CancellationToken token)
        {
            try
            {
                SetFeedbackAlpha(1f);

                float hold = Mathf.Max(0.01f, copiedFeedbackDuration);
                float elapsed = 0f;

                while (elapsed < hold)
                {
                    elapsed += Time.unscaledDeltaTime;

                    // 마지막 40%에서 사라진다.
                    float ratio = Mathf.Clamp01(elapsed / hold);
                    SetFeedbackAlpha(ratio < 0.6f ? 1f : Mathf.InverseLerp(1f, 0.6f, ratio));

                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }

                SetFeedbackAlpha(0f);
            }
            catch (OperationCanceledException)
            {
                // 패널이 꺼졌거나 연달아 복사한 정상 경로다. 최종 상태는 이어받은 쪽이 책임진다.
            }
        }

        private void SetFeedbackAlpha(float alpha)
        {
            if (copiedFeedback != null)
            {
                copiedFeedback.alpha = alpha;
            }
        }

        private int? ResolveSeed()
        {
            if (runBootstrapper == null)
            {
                // MonsterSpawnWaveProvider와 같은 방식의 폴백. 배선이 빠져도 화면은 뜨게 한다.
                runBootstrapper = FindFirstObjectByType<RunBootstrapper>();
            }

            if (runBootstrapper == null)
            {
                Debug.LogWarning($"[{nameof(ResultSummaryView)}] RunBootstrapper를 찾지 못해 시드를 표시하지 못했습니다.", this);

                return null;
            }

            // 초기화 전에는 MasterSeed가 0을 돌려준다. 그대로 쓰면 "시드 0"이라는
            // 있지도 않은 값을 플레이어에게 보여주고 복사까지 시키게 되므로 미확정과 구분한다.
            RunSeedContext context = runBootstrapper.SeedContext;

            if (context == null || !context.IsInitialized)
            {
                return null;
            }

            return runBootstrapper.MasterSeed;
        }

        private string ResolveWave()
        {
            DayNightManager dayNight = DayNightManager.Instance;

            if (dayNight == null)
            {
                return Unavailable;
            }

            // CurrentWave는 "지금 진행 중인 웨이브 번호"(1부터)다. 승리 경로는
            // WaveCompletionCoordinator가 EndNight를 건너뛰므로(최종 웨이브 클리어 = 승리)
            // 이 값이 그대로 "클리어한 최종 웨이브"가 되고, 패배는 "실패한 웨이브"가 된다.
            // 표시 의미는 양쪽 다 "판이 끝난 웨이브"로 일치한다 — 다만 이 숫자를 집계에
            // 쓰려는 쪽은 승패에 따라 클리어 수가 ±1 갈린다는 점을 알아야 한다(WL-223).
            return dayNight.CurrentWave.ToString();
        }

        private void SetText(TMP_Text label, string value)
        {
            if (label == null)
            {
                Debug.LogWarning($"[{nameof(ResultSummaryView)}] 표시 대상 텍스트가 배선되지 않았습니다.", this);

                return;
            }

            label.text = value;
        }
    }
}
