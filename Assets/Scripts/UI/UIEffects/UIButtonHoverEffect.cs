using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NorthLand.UI
{
    /// 버튼에 마우스를 올렸을 때 확대 + 하이라이트로 시인성을 올리는 공용 연출.
    /// 결과창 전용이 아니라 어느 UI 버튼에나 붙일 수 있다.
    ///
    /// ⚠ **시간축은 unscaled다.** 이 연출이 처음 필요해진 결과창은 `Time.timeScale`이 0인 화면이라
    /// (`GameSpeedController.HandleResultDecided`) scaled 타임으로 짜면 **호버해도 아무 반응이 없다.**
    /// 일시정지 중 메뉴에 붙일 때도 같은 이유로 unscaled여야 한다.
    ///
    /// ⚠ **파티클이 아니라 Graphic으로 만든다.** 씬의 캔버스가 전부 Screen Space - Overlay라
    /// `ParticleSystem`은 캔버스 위에 그려지지 않는다(`UIConfettiBurst`와 같은 근거).
    [RequireComponent(typeof(RectTransform))]
    public class UIButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("배선")]
        [SerializeField]
        [Tooltip("상호작용 가능 여부를 물어볼 대상. 비우면 이 오브젝트에서 찾는다.")]
        private Selectable target;

        [SerializeField]
        [Tooltip("확대할 대상. 비우면 이 오브젝트 자신.")]
        private RectTransform scaleRoot;

        [SerializeField]
        [Tooltip("호버 시 밝아지는 하이라이트. 버튼과 같은 스프라이트를 겹쳐 쓰는 것을 전제로 한다. 없어도 동작한다.")]
        private Graphic highlight;

        [Header("연출")]
        [SerializeField]
        [Tooltip("호버 시 확대 배율.")]
        private float hoverScale = 1.06f;

        [SerializeField]
        [Tooltip("하이라이트가 도달할 알파. 너무 높으면 버튼 아트가 씻겨 보인다.")]
        [Range(0f, 1f)]
        private float highlightAlpha = 0.3f;

        [SerializeField]
        [Tooltip("확대·하이라이트에 걸리는 시간(초).")]
        private float duration = 0.12f;

        [SerializeField]
        [Tooltip("호버 진입 시 재생할 효과음. 비우면 소리 없이 동작한다.")]
        private AudioClip hoverSfx;

        private Vector3 baseScale = Vector3.one;
        private bool hovered;

        private CancellationTokenSource animationCts;

        private void Awake()
        {
            if (scaleRoot == null)
            {
                scaleRoot = transform as RectTransform;
            }

            if (target == null)
            {
                target = GetComponent<Selectable>();
            }

            // 기준 배율은 authored 값을 정본으로 삼는다. 1로 가정하면 디자이너가 버튼을
            // 0.9로 눌러 둔 경우 호버 한 번에 크기가 튄다.
            baseScale = scaleRoot != null ? scaleRoot.localScale : Vector3.one;

            AdoptTargetSprite();
            SetHighlightAlpha(0f);
        }

        /// 하이라이트에 스프라이트가 없으면 버튼 것을 그대로 빌려 쓴다.
        ///
        /// 하이라이트는 버튼 위에 겹쳐 밝히는 판이라 **모양이 버튼과 같아야만** 한다. 스프라이트를
        /// 에디터에서 손으로 맞춰 두면 버튼 아트를 교체할 때마다 같이 바꿔야 하고, 빠뜨리면 둥근
        /// 버튼 위에 사각형이 번지는 형태로 조용히 어긋난다. 런타임에 따라가게 두면 그 축이 사라진다.
        private void AdoptTargetSprite()
        {
            if (highlight is not Image highlightImage || highlightImage.sprite != null)
            {
                return;
            }

            Graphic source = target != null ? target.targetGraphic : null;

            if (source is not Image sourceImage || sourceImage.sprite == null)
            {
                return;
            }

            highlightImage.sprite = sourceImage.sprite;
            highlightImage.type = sourceImage.type;
        }

        private void OnDisable()
        {
            // 패널이 꺼지는 순간 커서가 버튼 위에 있었다면 OnPointerExit이 오지 않는다.
            // 여기서 되돌리지 않으면 다음에 열릴 때 확대된 채로 떠 있다.
            CancelAnimation();

            hovered = false;

            if (scaleRoot != null)
            {
                scaleRoot.localScale = baseScale;
            }

            SetHighlightAlpha(0f);
        }

        private void OnDestroy()
        {
            CancelAnimation();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // 등장 연출 중이거나 비활성 버튼에는 반응하지 않는다 — 누를 수 없는 버튼이
            // 호버에 반응하면 "눌리는데 안 눌린다"로 읽힌다.
            if (target != null && !target.IsInteractable())
            {
                return;
            }

            if (hovered)
            {
                return;
            }

            hovered = true;

            if (hoverSfx != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(hoverSfx);
            }

            StartAnimation();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!hovered)
            {
                return;
            }

            hovered = false;

            StartAnimation();
        }

        private void StartAnimation()
        {
            CancelAnimation();

            animationCts = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());

            AnimateAsync(hovered, animationCts.Token).Forget();
        }

        private void CancelAnimation()
        {
            if (animationCts == null)
            {
                return;
            }

            animationCts.Cancel();
            animationCts.Dispose();
            animationCts = null;
        }

        private async UniTaskVoid AnimateAsync(bool entering, CancellationToken token)
        {
            // 진행 중이던 연출의 현재 값에서 이어 간다. 0에서 다시 시작하면 커서를
            // 빠르게 들락거릴 때 버튼이 튄다.
            Vector3 fromScale = scaleRoot != null ? scaleRoot.localScale : baseScale;
            Vector3 toScale = entering ? baseScale * hoverScale : baseScale;

            float fromAlpha = GetHighlightAlpha();
            float toAlpha = entering ? highlightAlpha : 0f;

            float span = Mathf.Max(0.01f, duration);
            float elapsed = 0f;

            try
            {
                while (elapsed < span)
                {
                    elapsed += Time.unscaledDeltaTime;

                    float ratio = Mathf.Clamp01(elapsed / span);
                    float eased = EaseOutCubic(ratio);

                    if (scaleRoot != null)
                    {
                        scaleRoot.localScale = Vector3.LerpUnclamped(fromScale, toScale, eased);
                    }

                    SetHighlightAlpha(Mathf.Lerp(fromAlpha, toAlpha, eased));

                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }

                if (scaleRoot != null)
                {
                    scaleRoot.localScale = toScale;
                }

                SetHighlightAlpha(toAlpha);
            }
            catch (OperationCanceledException)
            {
                // 다음 호버 연출이 이어받았거나 패널이 꺼진 정상 경로다.
                // 최종 상태는 이어받은 쪽이나 OnDisable이 책임진다.
            }
        }

        private float GetHighlightAlpha()
        {
            return highlight == null ? 0f : highlight.color.a;
        }

        private void SetHighlightAlpha(float alpha)
        {
            if (highlight == null)
            {
                return;
            }

            Color color = highlight.color;
            color.a = alpha;
            highlight.color = color;
        }

        private static float EaseOutCubic(float t)
        {
            float inv = 1f - t;

            return 1f - (inv * inv * inv);
        }
    }
}
