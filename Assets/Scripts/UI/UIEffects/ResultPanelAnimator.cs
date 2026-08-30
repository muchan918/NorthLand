using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace NorthLand.UI
{
    /// 승리/패배 결과 패널의 등장 연출. 패널 하나에 하나씩 붙는다.
    ///
    /// **상태를 스스로 판단하지 않는다.** 지금이 승리인지 패배인지는 `GameManager`가 알고
    /// `ResultUIManager`가 해당 패널을 켜면서 <see cref="Play"/>를 부른다 — 이쪽은 그 신호 하나만
    /// 받는다(`SpeedBoostEffect`와 같은 근거: 승패 상태가 두 곳에 생기면 "표시와 실제가 어긋난다"가
    /// 구조적으로 가능해진다).
    ///
    /// ⚠ **시간축은 전부 unscaled다.** 결과가 확정되는 순간 `GameSpeedController.HandleResultDecided`가
    /// `SetPaused(ResultDecided, true)`로 `Time.timeScale`을 0에 잠근다. scaled 타임으로 짜면 이 연출은
    /// **한 프레임도 재생되지 않고** 플레이어는 빈 암전 화면에 갇힌다. WL-100의 "플레이어에게 주는
    /// 안내·피드백은 unscaled" 기준과 같은 축이다.
    ///
    /// ⚠ **버튼은 어떤 경로로 끝나도 반드시 눌리는 상태로 돌아온다.** 연출 중에는 오조작을 막으려고
    /// 버튼을 잠그는데, 중간에 취소(패널 비활성·씬 전환)되면 잠긴 채로 남을 수 있다. 그래서 최종 상태
    /// 복원은 `finally`에 둔다 — 여기가 무너지면 플레이어가 결과창에서 빠져나갈 방법이 없어진다.
    public class ResultPanelAnimator : MonoBehaviour
    {
        [Header("연출 대상")]
        [SerializeField]
        [Tooltip("전체를 덮는 암전 배경. 비우면 이 오브젝트의 Image를 쓴다.")]
        private Image backdrop;

        [SerializeField]
        [Tooltip("떨어져 내려올 결과 로고(Victory/Defeat 아트).")]
        private RectTransform logo;

        [SerializeField]
        [Tooltip("로고의 Image. 알파 페이드에 쓴다. 비우면 logo에서 찾는다.")]
        private Image logoImage;

        [SerializeField]
        [Tooltip("차례로 올라올 버튼들. 배열 순서가 등장 순서다.")]
        private CanvasGroup[] buttons;

        [SerializeField]
        [Tooltip("승리 패널에만 배선한다. 로고가 착지하는 순간 터진다.")]
        private UIConfettiBurst confetti;

        [SerializeField]
        [Tooltip("도달 웨이브 배너. 로고가 착지한 뒤 도장처럼 찍히며 등장한다.")]
        private CanvasGroup waveInfo;

        [SerializeField]
        [Tooltip("좌하단 시드 표기. 부가 정보라 마지막에 조용히 떠오른다.")]
        private CanvasGroup seedInfo;

        [SerializeField]
        [Tooltip("시드·웨이브 숫자를 채우는 뷰. 연출 시작 시 한 번 읽는다.")]
        private ResultSummaryView summary;

        [Header("성격")]
        [SerializeField]
        [Tooltip("켜면 승리형(튀어오르며 안착), 끄면 패배형(무겁게 낙하 후 눌림).")]
        private bool triumphant = true;

        [SerializeField]
        [Tooltip("로고가 착지하는 순간 재생할 효과음. 비워 두면 소리 없이 진행한다.")]
        private AudioClip stinger;

        [SerializeField]
        [Tooltip("스팅어를 클립의 몇 초 지점부터 재생할지. 앞에 붙은 워밍업을 건너뛰어 타격을 착지에 맞춘다.")]
        private float stingerStartTime;

        [Header("타이밍(초)")]
        [SerializeField] private float backdropFadeDuration = 0.25f;
        [SerializeField] private float logoDropDuration = 0.45f;
        [SerializeField] private float shakeDuration = 0.15f;
        [SerializeField] private float waveStampDuration = 0.28f;
        [SerializeField] private float buttonFadeDuration = 0.2f;
        [SerializeField] private float buttonStagger = 0.08f;
        [SerializeField] private float seedFadeDuration = 0.25f;

        /// 웨이브 배너가 찍히기 시작하는 배율. 로고는 작은 데서 커지며 튀어오르고 배너는
        /// 큰 데서 줄어들며 꽂힌다 — 같은 방향으로 움직이면 두 연출이 서로 뭉갠다.
        private const float WaveStampFromScale = 1.25f;

        /// 로고가 낙하를 시작하는 높이(px). 승리는 더 높은 곳에서 튀어 들어오고 패배는 짧고 무겁게 떨어진다.
        private const float TriumphantDropHeight = 130f;
        private const float SombreDropHeight = 90f;

        /// 착지 흔들림 진폭(px). 배경이 아니라 **로고만** 흔든다 — 화면을 꽉 채운 암전 배경을
        /// 흔들면 가장자리에 빈 틈이 생긴다.
        private const float ShakeAmplitude = 6f;

        private float backdropTargetAlpha = 0.85f;
        private Vector2 logoRestPosition;

        /// 배너의 authored 배율. 1로 가정하면 레이아웃에서 크기를 조정해 둔 경우 연출이 그것을 덮어쓴다.
        private Vector3 waveInfoRestScale = Vector3.one;

        private CancellationTokenSource playCts;

        private void Awake()
        {
            if (backdrop == null)
            {
                backdrop = GetComponent<Image>();
            }

            if (logo != null && logoImage == null)
            {
                logoImage = logo.GetComponent<Image>();
            }

            if (logo == null || logoImage == null)
            {
                Debug.LogError($"[{nameof(ResultPanelAnimator)}] 로고가 배선되지 않았습니다.", this);
                enabled = false;
                return;
            }

            // 배경의 목표 알파는 아티스트가 프리팹에 authored한 값을 정본으로 삼는다.
            if (backdrop != null)
            {
                backdropTargetAlpha = backdrop.color.a;
            }

            logoRestPosition = logo.anchoredPosition;

            if (waveInfo != null && waveInfo.transform is RectTransform waveRect)
            {
                waveInfoRestScale = waveRect.localScale;
            }
        }

        private void OnDisable()
        {
            // UniTask는 코루틴과 달리 GameObject가 꺼져도 계속 돈다. 여기서 끊지 않으면
            // 꺼진 패널의 연출이 배후에서 이어지고, 다음 표시 때 중간 상태가 한 프레임 비친다.
            CancelPlay();
        }

        private void OnDestroy()
        {
            CancelPlay();
        }

        /// 결과 패널이 켜졌다. `ResultUIManager`가 패널을 활성화한 직후 부른다.
        public void Play()
        {
            if (!enabled)
            {
                return;
            }

            CancelPlay();

            playCts = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());

            PlayAsync(playCts.Token).Forget();
        }

        private void CancelPlay()
        {
            if (playCts == null)
            {
                return;
            }

            playCts.Cancel();
            playCts.Dispose();
            playCts = null;
        }

        private async UniTaskVoid PlayAsync(CancellationToken token)
        {
            try
            {
                // 값은 연출이 시작되기 전에 채운다. 배너가 등장하는 동안 숫자가 바뀌면
                // 플레이어가 "몇 웨이브였지?"를 다시 읽어야 한다.
                if (summary != null)
                {
                    summary.Bind();
                }

                ApplyStartState();

                await FadeBackdropAsync(token);
                await DropLogoAsync(token);

                OnLogoLanded();

                await ShakeLogoAsync(token);
                await StampWaveInfoAsync(token);
                await RevealButtonsAsync(token);
                await FadeSeedInfoAsync(token);
            }
            catch (OperationCanceledException)
            {
                // 패널이 꺼졌거나 씬이 전환된 정상 경로다.
            }
            finally
            {
                // 취소든 정상 종료든 최종 상태를 보장한다. 버튼이 잠긴 채 남으면
                // 플레이어가 결과창에서 나갈 수 없다.
                ApplyEndState();
            }
        }

        /// 연출 시작 직전의 화면: 모두 투명하고, 로고는 위에 떠 있고, 버튼은 잠겨 있다.
        private void ApplyStartState()
        {
            SetBackdropAlpha(0f);
            SetLogoAlpha(0f);

            float dropHeight = triumphant ? TriumphantDropHeight : SombreDropHeight;

            logo.anchoredPosition = logoRestPosition + new Vector2(0f, dropHeight);
            logo.localScale = Vector3.one * (triumphant ? 0.55f : 1.06f);

            if (waveInfo != null)
            {
                waveInfo.alpha = 0f;

                if (waveInfo.transform is RectTransform waveRect)
                {
                    waveRect.localScale = waveInfoRestScale * WaveStampFromScale;
                }
            }

            if (seedInfo != null)
            {
                seedInfo.alpha = 0f;
            }

            if (buttons == null)
            {
                return;
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                {
                    continue;
                }

                buttons[i].alpha = 0f;
                buttons[i].interactable = false;
                buttons[i].blocksRaycasts = false;
            }
        }

        /// 연출이 끝났거나 취소됐을 때의 화면: 전부 제자리, 버튼은 눌린다.
        private void ApplyEndState()
        {
            if (this == null || logo == null)
            {
                return;
            }

            SetBackdropAlpha(backdropTargetAlpha);
            SetLogoAlpha(1f);

            logo.anchoredPosition = logoRestPosition;
            logo.localScale = Vector3.one;

            if (waveInfo != null)
            {
                waveInfo.alpha = 1f;

                if (waveInfo.transform is RectTransform waveRect)
                {
                    waveRect.localScale = waveInfoRestScale;
                }
            }

            if (seedInfo != null)
            {
                seedInfo.alpha = 1f;
            }

            if (buttons == null)
            {
                return;
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                {
                    continue;
                }

                buttons[i].alpha = 1f;
                buttons[i].interactable = true;
                buttons[i].blocksRaycasts = true;
            }
        }

        private async UniTask FadeBackdropAsync(CancellationToken token)
        {
            float duration = Mathf.Max(0.01f, backdropFadeDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                float ratio = Mathf.Clamp01(elapsed / duration);
                SetBackdropAlpha(Mathf.Lerp(0f, backdropTargetAlpha, ratio));

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            SetBackdropAlpha(backdropTargetAlpha);
        }

        private async UniTask DropLogoAsync(CancellationToken token)
        {
            float duration = Mathf.Max(0.01f, logoDropDuration);
            float dropHeight = triumphant ? TriumphantDropHeight : SombreDropHeight;
            float startScale = triumphant ? 0.55f : 1.06f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                float ratio = Mathf.Clamp01(elapsed / duration);

                // 승리는 튀어오르며 안착(오버슈트), 패배는 가속하며 떨어진다(오버슈트 없음).
                float eased = triumphant ? EaseOutBack(ratio) : EaseInQuad(ratio);

                logo.anchoredPosition = logoRestPosition + new Vector2(0f, Mathf.Lerp(dropHeight, 0f, eased));
                logo.localScale = Vector3.one * Mathf.LerpUnclamped(startScale, 1f, eased);

                // 알파는 앞쪽에서 빠르게 올린다 — 끝까지 반투명하면 낙하가 흐릿하게 읽힌다.
                SetLogoAlpha(Mathf.Clamp01(ratio / 0.4f));

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            logo.anchoredPosition = logoRestPosition;
            logo.localScale = Vector3.one;
            SetLogoAlpha(1f);
        }

        private void OnLogoLanded()
        {
            if (stinger != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfxExclusive(stinger, 1f, stingerStartTime);
            }

            if (triumphant && confetti != null)
            {
                confetti.Burst();
            }
        }

        private async UniTask ShakeLogoAsync(CancellationToken token)
        {
            float duration = Mathf.Max(0.01f, shakeDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                float ratio = Mathf.Clamp01(elapsed / duration);
                float falloff = 1f - ratio;

                logo.anchoredPosition = logoRestPosition
                    + (UnityEngine.Random.insideUnitCircle * (ShakeAmplitude * falloff));

                // 패배는 반죽이 눌리듯 살짝 찌그러졌다 돌아온다.
                if (!triumphant)
                {
                    float squash = 1f - (0.04f * falloff);
                    logo.localScale = new Vector3(1f + (0.03f * falloff), squash, 1f);
                }

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            logo.anchoredPosition = logoRestPosition;
            logo.localScale = Vector3.one;
        }

        /// 웨이브 배너를 도장처럼 찍는다. 큰 배율에서 제자리로 줄어들며 나타난다.
        private async UniTask StampWaveInfoAsync(CancellationToken token)
        {
            if (waveInfo == null)
            {
                return;
            }

            var rect = waveInfo.transform as RectTransform;
            Vector3 restScale = rect != null ? waveInfoRestScale : Vector3.one;

            float duration = Mathf.Max(0.01f, waveStampDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                float ratio = Mathf.Clamp01(elapsed / duration);
                float eased = EaseOutCubic(ratio);

                waveInfo.alpha = Mathf.Clamp01(ratio / 0.5f);

                if (rect != null)
                {
                    rect.localScale = restScale * Mathf.LerpUnclamped(WaveStampFromScale, 1f, eased);
                }

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            waveInfo.alpha = 1f;

            if (rect != null)
            {
                rect.localScale = restScale;
            }
        }

        /// 좌하단 시드 표기를 조용히 띄운다. 부가 정보라 움직임 없이 알파만 올린다.
        private async UniTask FadeSeedInfoAsync(CancellationToken token)
        {
            if (seedInfo == null)
            {
                return;
            }

            float duration = Mathf.Max(0.01f, seedFadeDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                seedInfo.alpha = EaseOutCubic(Mathf.Clamp01(elapsed / duration));

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            seedInfo.alpha = 1f;
        }

        private async UniTask RevealButtonsAsync(CancellationToken token)
        {
            if (buttons == null || buttons.Length == 0)
            {
                return;
            }

            float duration = Mathf.Max(0.01f, buttonFadeDuration);
            float stagger = Mathf.Max(0f, buttonStagger);

            for (int i = 0; i < buttons.Length; i++)
            {
                CanvasGroup group = buttons[i];

                if (group == null)
                {
                    continue;
                }

                var rect = group.transform as RectTransform;
                Vector2 rest = rect != null ? rect.anchoredPosition : Vector2.zero;
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;

                    float ratio = Mathf.Clamp01(elapsed / duration);
                    float eased = EaseOutCubic(ratio);

                    group.alpha = eased;

                    if (rect != null)
                    {
                        rect.anchoredPosition = rest + new Vector2(0f, Mathf.Lerp(-16f, 0f, eased));
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }

                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;

                if (rect != null)
                {
                    rect.anchoredPosition = rest;
                }

                if (stagger > 0f && i < buttons.Length - 1)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(stagger),
                        DelayType.UnscaledDeltaTime,
                        PlayerLoopTiming.Update,
                        token);
                }
            }
        }

        private void SetBackdropAlpha(float alpha)
        {
            if (backdrop == null)
            {
                return;
            }

            Color color = backdrop.color;
            color.a = alpha;
            backdrop.color = color;
        }

        private void SetLogoAlpha(float alpha)
        {
            if (logoImage == null)
            {
                return;
            }

            Color color = logoImage.color;
            color.a = alpha;
            logoImage.color = color;
        }

        private static float EaseOutBack(float t)
        {
            const float C1 = 1.70158f;
            const float C3 = C1 + 1f;

            float inv = t - 1f;

            return 1f + (C3 * inv * inv * inv) + (C1 * inv * inv);
        }

        private static float EaseInQuad(float t)
        {
            return t * t;
        }

        private static float EaseOutCubic(float t)
        {
            float inv = 1f - t;

            return 1f - (inv * inv * inv);
        }
    }
}
