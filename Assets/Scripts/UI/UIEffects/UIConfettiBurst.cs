using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace NorthLand.UI
{
    /// 결과창 승리 연출의 컨페티(스프링클) 버스트.
    ///
    /// **파티클이 아니라 uGUI `Image`로 만든다.** 씬의 모든 캔버스가 Screen Space - Overlay이고
    /// `ResultCanvas`는 그중 최상단(sortingOrder 900)이다. `ParticleSystem`은 씬 렌더러라
    /// Overlay 캔버스보다 위에 그려질 방법이 없어서, 파티클로 만들면 컨페티가 결과창의
    /// 85% 암전 배경 **뒤에** 깔려 사실상 보이지 않는다. 캔버스 안의 Graphic으로 만들면
    /// 정렬이 캔버스 자식 순서로 결정되므로 이 문제가 원천적으로 없다.
    ///
    /// ⚠ **시간축은 unscaled다.** 결과가 확정되면 `GameSpeedController`가
    /// `Time.timeScale`을 0으로 잠그므로(`HandleResultDecided`), scaled 타임으로 짜면
    /// 연출이 **한 프레임도 재생되지 않는다**.
    public class UIConfettiBurst : MonoBehaviour
    {
        /// 조각 하나의 물리 상태. 클래스가 아니라 구조체 배열로 두어 매 버스트마다 할당이 생기지 않게 한다.
        private struct Piece
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Rotation;
            public float AngularVelocity;
            public float Lifetime;
        }

        [Header("배선")]
        [SerializeField]
        [Tooltip("조각이 생성될 부모. 비우면 이 오브젝트 자신을 쓴다.")]
        private RectTransform container;

        [SerializeField]
        [Tooltip("조각 스프라이트. 비우면 사각형으로 그려진다(내장 UISprite 권장).")]
        private Sprite pieceSprite;

        [Header("발사")]
        [SerializeField]
        [Tooltip("조각 개수. 발사 원점마다 절반씩 나눠 쏜다.")]
        private int pieceCount = 36;

        [SerializeField]
        [Tooltip("컨테이너 기준 발사 원점. 보통 로고 좌·우 아래 모서리 두 곳.")]
        private Vector2[] origins =
        {
            new Vector2(-420f, 60f),
            new Vector2(420f, 60f),
        };

        [SerializeField]
        [Tooltip("발사 속도 범위(px/초).")]
        private Vector2 speedRange = new Vector2(900f, 1500f);

        [SerializeField]
        [Tooltip("위쪽 기준 발사각 반각(도). 클수록 넓게 퍼진다.")]
        private float spreadDegrees = 42f;

        [SerializeField]
        [Tooltip("중력(px/초^2). 클수록 빨리 떨어진다.")]
        private float gravity = 2600f;

        [SerializeField]
        [Tooltip("조각이 살아 있는 시간(초).")]
        private float lifetime = 1.7f;

        /// 로고의 캔디 팔레트에서 뽑은 색. 아트와 톤을 맞추기 위한 값이라 인스펙터로 열지 않는다.
        private static readonly Color[] Palette =
        {
            new Color(0.96f, 0.90f, 0.78f), // 크림
            new Color(0.94f, 0.55f, 0.62f), // 핑크
            new Color(0.85f, 0.29f, 0.29f), // 레드
            new Color(0.96f, 0.78f, 0.26f), // 옐로
            new Color(0.49f, 0.80f, 0.56f), // 민트
            new Color(0.72f, 0.61f, 0.88f), // 라벤더
            new Color(0.49f, 0.76f, 0.91f), // 스카이
        };

        private RectTransform[] pieceTransforms;
        private Image[] pieceImages;
        private Piece[] pieces;

        private CancellationTokenSource burstCts;

        private void Awake()
        {
            if (container == null)
            {
                container = transform as RectTransform;
            }

            if (container == null)
            {
                Debug.LogError($"[{nameof(UIConfettiBurst)}] RectTransform이 아닌 곳에 붙어 있습니다.", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            // 패널이 꺼지면 즉시 멈춘다. UniTask는 코루틴과 달리 GameObject가 꺼져도 계속 돌기 때문에
            // 여기서 끊지 않으면 꺼진 패널의 조각을 계속 움직이고, 다음 표시 때 옛 조각이 한 프레임 스친다.
            CancelBurst();
            HideAll();
        }

        private void OnDestroy()
        {
            CancelBurst();
        }

        /// 컨페티를 터뜨린다. 재생 중에 다시 부르면 이전 버스트를 버리고 처음부터 다시 쏜다.
        public void Burst()
        {
            if (!enabled)
            {
                return;
            }

            CancelBurst();

            burstCts = CancellationTokenSource.CreateLinkedTokenSource(
                this.GetCancellationTokenOnDestroy());

            BurstAsync(burstCts.Token).Forget();
        }

        private void CancelBurst()
        {
            if (burstCts == null)
            {
                return;
            }

            burstCts.Cancel();
            burstCts.Dispose();
            burstCts = null;
        }

        private async UniTaskVoid BurstAsync(CancellationToken token)
        {
            try
            {
                EnsurePool();
                Launch();

                float elapsed = 0f;
                float span = Mathf.Max(0.01f, lifetime);

                while (elapsed < span)
                {
                    float dt = Time.unscaledDeltaTime;
                    elapsed += dt;

                    Step(dt);

                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }

                HideAll();
            }
            catch (OperationCanceledException)
            {
                // 패널이 꺼졌거나 씬이 전환된 정상 경로다. 파괴된 Image를 건드리지 않고 그대로 빠져나간다.
            }
        }

        /// 조각을 필요한 만큼 한 번만 만들어 두고 이후 버스트는 재사용한다.
        private void EnsurePool()
        {
            int count = Mathf.Max(0, pieceCount);

            if (pieceTransforms != null && pieceTransforms.Length == count)
            {
                return;
            }

            if (pieceTransforms != null)
            {
                for (int i = 0; i < pieceTransforms.Length; i++)
                {
                    if (pieceTransforms[i] != null)
                    {
                        Destroy(pieceTransforms[i].gameObject);
                    }
                }
            }

            pieceTransforms = new RectTransform[count];
            pieceImages = new Image[count];
            pieces = new Piece[count];

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Piece{i:00}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rt = (RectTransform)go.transform;

                rt.SetParent(container, false);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);

                var image = go.GetComponent<Image>();
                image.sprite = pieceSprite;

                // 결과창 버튼 위를 조각이 지나가므로 반드시 꺼야 한다. 켜져 있으면
                // 컨페티가 "다시하기" 클릭을 삼킨다.
                image.raycastTarget = false;

                pieceTransforms[i] = rt;
                pieceImages[i] = image;

                go.SetActive(false);
            }
        }

        private void Launch()
        {
            if (pieceTransforms == null || pieceTransforms.Length == 0)
            {
                return;
            }

            Vector2[] launchPoints = origins != null && origins.Length > 0
                ? origins
                : new[] { Vector2.zero };

            for (int i = 0; i < pieceTransforms.Length; i++)
            {
                Vector2 origin = launchPoints[i % launchPoints.Length];

                // 원점이 화면 중앙에서 벗어난 쪽으로 기울여 쏜다 — 좌우 두 발이 서로 바깥으로 퍼진다.
                float bias = Mathf.Sign(origin.x == 0f ? 1f : origin.x);
                float angle = 90f - (bias * UnityEngine.Random.Range(0f, spreadDegrees));
                float speed = UnityEngine.Random.Range(speedRange.x, speedRange.y);
                float radians = angle * Mathf.Deg2Rad;

                pieces[i] = new Piece
                {
                    Position = origin,
                    Velocity = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * speed,
                    Rotation = UnityEngine.Random.Range(0f, 360f),
                    AngularVelocity = UnityEngine.Random.Range(-540f, 540f),
                    Lifetime = 0f,
                };

                // 절반은 길쭉한 스프링클, 절반은 작은 사탕 조각으로 섞는다.
                Vector2 size = (i % 2 == 0)
                    ? new Vector2(UnityEngine.Random.Range(16f, 24f), UnityEngine.Random.Range(6f, 9f))
                    : new Vector2(UnityEngine.Random.Range(9f, 13f), UnityEngine.Random.Range(9f, 13f));

                pieceTransforms[i].sizeDelta = size;
                pieceTransforms[i].anchoredPosition = origin;
                pieceTransforms[i].localRotation = Quaternion.Euler(0f, 0f, pieces[i].Rotation);
                pieceTransforms[i].gameObject.SetActive(true);

                pieceImages[i].color = Palette[UnityEngine.Random.Range(0, Palette.Length)];
            }
        }

        private void Step(float deltaTime)
        {
            float span = Mathf.Max(0.01f, lifetime);

            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieceTransforms[i] == null)
                {
                    continue;
                }

                pieces[i].Lifetime += deltaTime;
                pieces[i].Velocity.y -= gravity * deltaTime;
                pieces[i].Position += pieces[i].Velocity * deltaTime;
                pieces[i].Rotation += pieces[i].AngularVelocity * deltaTime;

                pieceTransforms[i].anchoredPosition = pieces[i].Position;
                pieceTransforms[i].localRotation = Quaternion.Euler(0f, 0f, pieces[i].Rotation);

                // 마지막 35%에서 서서히 사라진다 — 조각이 화면 밖으로 나가기 전에 툭 끊기는 것을 막는다.
                float ratio = Mathf.Clamp01(pieces[i].Lifetime / span);
                float alpha = ratio < 0.65f ? 1f : Mathf.InverseLerp(1f, 0.65f, ratio);

                Color color = pieceImages[i].color;
                color.a = alpha;
                pieceImages[i].color = color;
            }
        }

        private void HideAll()
        {
            if (pieceTransforms == null)
            {
                return;
            }

            for (int i = 0; i < pieceTransforms.Length; i++)
            {
                if (pieceTransforms[i] != null)
                {
                    pieceTransforms[i].gameObject.SetActive(false);
                }
            }
        }
    }
}
