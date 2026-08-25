using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NorthLand.Core
{
    /// <summary>
    /// 로딩 씬의 표시부. 진행률을 받아 그리고, 마지막에 커튼을 걷는다.
    /// 진행 판단·순서는 전부 <see cref="LoadingFlow"/>가 가진다 — 이쪽은 값을 받아 그리기만 한다.
    ///
    /// 참조는 <b>전부 선택</b>이다. 마스코트 애니메이션만 도는 씬에서도 흐름이 성립해야 하므로,
    /// 없는 참조는 조용히 건너뛴다.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        [Header("Curtain")]
        [Tooltip("커튼 전체를 감싸는 CanvasGroup. 페이드 아웃에 쓴다. 없으면 즉시 사라진다.")]
        [SerializeField] CanvasGroup curtain;

        [Tooltip("커튼이 걷히는 시간(초). Time.timeScale의 영향을 받지 않는다.")]
        [SerializeField, Min(0f)] float fadeOutSeconds = 0.35f;

        [Header("Progress")]
        [Tooltip("진행률 표시. 무작위 문구에 분홍 액체가 차오르는 높이가 곧 진행률이다.")]
        [SerializeField] LoadingTipText tipText;

        [Tooltip("채움 표시가 목표값을 따라가는 속도(초당 비율). 0이면 즉시 반영한다.")]
        [SerializeField, Min(0f)] float progressLerpPerSecond = 2.5f;

        /// 흐름이 보고한 목표 진행률. 표시값은 이 값을 뒤따라간다.
        private float targetProgress;
        private float shownProgress;

        private void Awake()
        {
            // 커튼은 처음부터 완전히 덮고 있어야 한다. 씬에서 알파를 잘못 저장해도 여기서 바로잡는다.
            if (curtain != null)
            {
                curtain.alpha = 1f;
                curtain.blocksRaycasts = true;
            }

            Apply(0f);
        }

        private void Update()
        {
            if (Mathf.Approximately(shownProgress, targetProgress)) return;

            // ⚠ unscaledDeltaTime이어야 한다. 로딩 중 timeScale이 0이거나 배속이 걸려 있어도
            //    진행률 표시는 실시간으로 움직여야 한다(연출 시간축 규약 — SystemMap §6).
            shownProgress = progressLerpPerSecond <= 0f
                ? targetProgress
                : Mathf.MoveTowards(
                    shownProgress,
                    targetProgress,
                    progressLerpPerSecond * Time.unscaledDeltaTime);

            Apply(shownProgress);
        }

        /// <summary>흐름이 보고하는 진행률(0~1). 표시값은 부드럽게 뒤따라간다.</summary>
        public void ReportProgress(float value)
        {
            targetProgress = Mathf.Clamp01(value);
        }

        /// <summary>표시값을 목표까지 즉시 당긴다. 커튼을 걷기 직전에 100%를 보장하려고 쓴다.</summary>
        public void SnapProgress()
        {
            shownProgress = targetProgress;
            Apply(shownProgress);
        }

        /// <summary>
        /// 커튼을 걷는다. 완료될 때까지 프레임을 돌며, 끝나면 레이캐스트 차단도 푼다.
        /// <see cref="LoadingFlow"/>가 게임 씬을 활성으로 바꾼 뒤에 부른다.
        /// </summary>
        public async UniTask FadeOutAsync(CancellationToken cancellationToken)
        {
            if (curtain == null) return;

            if (fadeOutSeconds <= 0f)
            {
                curtain.alpha = 0f;
                curtain.blocksRaycasts = false;

                return;
            }

            float elapsed = 0f;
            float start = curtain.alpha;

            while (elapsed < fadeOutSeconds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                elapsed += Time.unscaledDeltaTime;
                curtain.alpha = Mathf.Lerp(start, 0f, elapsed / fadeOutSeconds);

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            curtain.alpha = 0f;
            curtain.blocksRaycasts = false;
        }

        private void Apply(float value)
        {
            if (tipText != null) tipText.SetProgress(value);
        }
    }
}
