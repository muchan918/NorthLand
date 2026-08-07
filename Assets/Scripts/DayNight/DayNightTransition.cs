using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 낮/밤 전환 연출 (#101). 화면을 셀로 나눠 대각선으로 뒤집으면서 씬을 함께 보간한다.
///
/// 역할 분담:
/// - 씬 라이팅·앰비언트·스카이박스·볼륨 weight·물 틴트 = DayNightLightingController.ApplyBlend(전역)
/// - 가로등                                          = StreetLampController.SetBlend(전역)
/// - "셀이 먼저 밤이 되는" 부분만                     = NightWipe 풀스크린 패스
///
/// 왜 이렇게 나누는가: 밤 전환에서 화면공간인 건 볼륨 그레이드뿐이고 나머지는 전부 씬 라이팅이라
/// "이 셀만 밤"이 원리적으로 불가능하다. 그래서 씬은 전역으로 보간하고, 셰이더는 아직 씬이
/// 채우지 못한 나머지(_Amount)를 뒤집힌 셀에만 얹어 그 칸을 먼저 목표 상태로 보이게 만든다.
///
/// 전환이 끝나면 씬 블렌드가 목표에 도달해 _Amount가 0이 되므로, 셰이더의 그레이드 근사식이
/// URP ColorAdjustments와 정확히 일치하지 않아도 종료 시점에 튀지 않는다.
///
/// HUD는 덮이지 않는다 — 이 패스는 카메라 렌더 안에서 돌고, ScreenSpaceOverlay 캔버스는 그 뒤에
/// 그려지기 때문이다(구조상 자동으로 보장된다).
/// </summary>
public class DayNightTransition : MonoBehaviour
{
    private static readonly int ProgressId = Shader.PropertyToID("_NightWipe_Progress");
    private static readonly int AmountId = Shader.PropertyToID("_NightWipe_Amount");
    private static readonly int CellSizeId = Shader.PropertyToID("_NightWipe_CellSize");
    private static readonly int JitterId = Shader.PropertyToID("_NightWipe_Jitter");
    private static readonly int ReverseId = Shader.PropertyToID("_NightWipe_Reverse");
    private static readonly int PostExposureId = Shader.PropertyToID("_NightWipe_PostExposure");
    private static readonly int ColorFilterId = Shader.PropertyToID("_NightWipe_ColorFilter");
    private static readonly int SaturationId = Shader.PropertyToID("_NightWipe_Saturation");
    private static readonly int ContrastId = Shader.PropertyToID("_NightWipe_Contrast");
    private static readonly int EdgeGlowId = Shader.PropertyToID("_NightWipe_EdgeGlow");
    private static readonly int EdgeWidthId = Shader.PropertyToID("_NightWipe_EdgeWidth");
    private static readonly int EdgeColorId = Shader.PropertyToID("_NightWipe_EdgeColor");

    [Header("References")]
    [SerializeField] private DayNightLightingController lighting;
    [SerializeField] private StreetLampController streetLamps;

    // PC_Renderer의 "Night Wipe" 피처(서브에셋). 전환 중에만 켜서 평소 프레임 비용을 0으로 둔다.
    [SerializeField]
    [Tooltip("PC_Renderer 안의 Night Wipe 렌더러 피처")]
    private ScriptableRendererFeature nightWipeFeature;

    [Header("타이밍")]
    [SerializeField] private float duration = 0.8f;

    [Tooltip("와이프 진행 커브. 기본 선형.")]
    [SerializeField] private AnimationCurve ease = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("셀")]
    [Tooltip("화면 픽셀 단위 셀 한 변")]
    [Min(1f)]
    [SerializeField] private float cellSize = 48f;

    [Tooltip("셀별 임계값 흔들기. 0이면 경계가 직선, 크면 들쭉날쭉 흩뿌려진다.")]
    [Range(0f, 1f)]
    [SerializeField] private float jitter = 0.35f;

    [Tooltip("밤으로 갈 때 우하단에서 좌상단으로 진행한다. 낮으로 갈 때는 반대 방향으로 뒤집는다.")]
    [SerializeField] private bool reverseWhenGoingToDay = true;

    // 셰이더가 쓰는 밤 그레이드 근사. NightLookProfile의 ColorAdjustments와 대략 맞춰 두면
    // 전환 도중 셀이 최종 밤과 비슷하게 보인다(정확히 일치할 필요는 없다 — 위 주석 참고).
    [Header("밤 그레이드 (셰이더 근사)")]
    [SerializeField] private float postExposure = -0.75f;
    [SerializeField] private Color colorFilter = new Color(0.66f, 0.76f, 1f);
    [SerializeField] private float saturation = -0.08f;
    [SerializeField] private float contrast = 0.05f;

    [Header("선행 엣지")]
    [Range(0f, 2f)]
    [SerializeField] private float edgeGlow = 0.35f;

    [Range(0.001f, 0.5f)]
    [SerializeField] private float edgeWidth = 0.08f;

    [SerializeField] private Color edgeColor = new Color(0.65f, 0.78f, 1f);

    /// <summary>전환이 진행 중인가. #101의 입력·트리거 잠금이 이 값을 본다.</summary>
    public bool IsTransitioning { get; private set; }

    /// <summary>전환이 끝난 직후 발생. 몬스터 스폰처럼 "화면이 다 바뀐 뒤"에 시작해야 하는 것이 구독한다.</summary>
    public event Action OnTransitionComplete;

    // 0 = 낮, 1 = 밤. 전환이 중간에 잘렸을 때 이어서 시작하기 위해 들고 있는다.
    private float _blend;
    private CancellationTokenSource _cts;

    private void Awake()
    {
        SetFeatureActive(false);
    }

    private void Start()
    {
        if (DayNightManager.Instance == null)
        {
            Debug.LogError("DayNightManager 없음", this);
            return;
        }

        _blend = DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Night ? 1f : 0f;

        DayNightManager.Instance.OnDayToNight += HandleDayToNight;
        DayNightManager.Instance.OnNightToDay += HandleNightToDay;
    }

    private void OnDestroy()
    {
        if (DayNightManager.Instance != null)
        {
            DayNightManager.Instance.OnDayToNight -= HandleDayToNight;
            DayNightManager.Instance.OnNightToDay -= HandleNightToDay;
        }

        CancelRunning();
        SetFeatureActive(false);
    }

    private void HandleDayToNight() => PlayAsync(1f).Forget();
    private void HandleNightToDay() => PlayAsync(0f).Forget();

    /// <summary>
    /// 현재 상태에서 target(0=낮, 1=밤)까지 전환한다. 진행 중이던 전환은 취소하고 그 목표를
    /// 즉시 확정한 뒤 새로 시작한다 — 이전 Lerp가 남아 두 전환이 겹쳐 도는 것을 막는다(#101).
    /// </summary>
    public async UniTask PlayAsync(float target)
    {
        CancelRunning();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        CancellationToken token = _cts.Token;

        float from = _blend;

        if (Mathf.Approximately(from, target) || duration <= 0f)
        {
            ApplyScene(target);
            _blend = target;
            SetFeatureActive(false);
            IsTransitioning = false;
            OnTransitionComplete?.Invoke();

            return;
        }

        IsTransitioning = true;
        PushStaticParams(target);
        SetFeatureActive(true);

        try
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();

                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = ease != null ? ease.Evaluate(progress) : progress;

                float sceneBlend = Mathf.LerpUnclamped(from, target, eased);
                ApplyScene(sceneBlend);

                Shader.SetGlobalFloat(ProgressId, eased);
                // 뒤집힌 셀은 목표 상태로 보여야 하므로, 씬이 아직 못 채운 나머지를 셰이더가 얹는다.
                Shader.SetGlobalFloat(AmountId, target - sceneBlend);

                await UniTask.Yield(PlayerLoopTiming.Update, token);

                elapsed += Time.deltaTime;
            }

            ApplyScene(target);
            _blend = target;
        }
        catch (OperationCanceledException)
        {
            // 취소는 CancelRunning이 목표를 확정한 뒤에만 일어난다 — 여기서 상태를 되돌리지 않는다.
            throw;
        }
        finally
        {
            Shader.SetGlobalFloat(ProgressId, 0f);
            Shader.SetGlobalFloat(AmountId, 0f);
            SetFeatureActive(false);
            IsTransitioning = false;
        }

        OnTransitionComplete?.Invoke();
    }

    /// <summary>
    /// 진행 중인 전환을 취소하고, 그 전환의 목표 상태를 즉시 확정한다.
    /// 중간 상태로 멈추면 라이팅이 어중간한 값에 남아 다음 전환의 시작점이 어긋난다.
    /// </summary>
    private void CancelRunning()
    {
        if (_cts == null) return;

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
    }

    private void ApplyScene(float blend)
    {
        _blend = blend;

        if (lighting != null) lighting.ApplyBlend(blend);
        if (streetLamps != null) streetLamps.SetBlend(blend);
    }

    private void PushStaticParams(float target)
    {
        Shader.SetGlobalFloat(CellSizeId, cellSize);
        Shader.SetGlobalFloat(JitterId, jitter);

        // 밤으로 갈 때는 우하단 -> 좌상단, 낮으로 갈 때는 반대.
        bool reverse = reverseWhenGoingToDay && target < 0.5f;
        Shader.SetGlobalFloat(ReverseId, reverse ? 1f : 0f);

        Shader.SetGlobalFloat(PostExposureId, postExposure);
        Shader.SetGlobalColor(ColorFilterId, colorFilter);
        Shader.SetGlobalFloat(SaturationId, saturation);
        Shader.SetGlobalFloat(ContrastId, contrast);

        Shader.SetGlobalFloat(EdgeGlowId, edgeGlow);
        Shader.SetGlobalFloat(EdgeWidthId, edgeWidth);
        Shader.SetGlobalColor(EdgeColorId, edgeColor);
    }

    private void SetFeatureActive(bool active)
    {
        if (nightWipeFeature == null) return;

        nightWipeFeature.SetActive(active);
    }
}
