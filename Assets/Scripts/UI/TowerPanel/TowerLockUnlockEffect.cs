using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 잠금 오버레이(`TowerLockOverlay`)의 해제 연출 — 자물쇠가 떨리다 팡 터지며 오버레이가 걷힌다.
/// 재생이 끝나면 자기 게임오브젝트를 끈다.
///
/// <para><b>왜 상태가 아니라 연출만 갖는가</b><br/>
/// 잠금 여부는 <see cref="TowerSelectPanelView"/>가 매 갱신마다 웨이브로 **계산**한다(#424).
/// 이 컴포넌트는 "잠김 → 열림"으로 넘어간 그 순간에만 불리며, 자기가 열렸는지 여부를 기억하지
/// 않는다. 기억하면 세이브 복원 경로에서 두 값이 갈라진다.</para>
///
/// <para><b>왜 코루틴이 아니라 UniTask인가</b><br/>
/// 해금 순간에 낮 패널이 아직 비활성일 수 있다 — `EndNight`이 `WaveCount++` 직후 `OnNightToDay`를
/// 쏘고, 그걸 받은 `ManagementController`가 웨이브 클리어 마나석을 지급하면서 자원 갱신 경로로
/// `RefreshButtons`가 먼저 돈다(낮 패널을 켜는 `OnDayStart`보다 앞). `StartCoroutine`은 그 시점에
/// "game object is inactive"로 실패해 전이만 소비됐다. UniTask는 PlayerLoop에서 돌아 활성 상태와
/// 무관하고, 아래 `WaitUntil` 한 줄이 "보일 때 재생"을 명시로 남긴다 —
/// 예약 플래그와 `OnEnable` 핸드오프가 통째로 필요 없어진다.</para>
///
/// <para><b>왜 이징을 코드 상수로 두지 않는가</b><br/>
/// back-out 계수가 이미 저장소에 두 벌 있다(`VfxScaleHold`, `TerritoryNodeStateVisual` — WL-085).
/// 세 번째를 만들지 않으려고 곡선을 <see cref="AnimationCurve"/>로 노출했다. 연출 튜닝이
/// 인스펙터에서 끝나는 부수 효과도 있다.</para>
/// </summary>
[DisallowMultipleComponent]
public class TowerLockUnlockEffect : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("오버레이 전체 페이드용 (이 오브젝트의 CanvasGroup)")]
    [SerializeField] CanvasGroup _group;
    [Tooltip("떨리고 팡 하는 자물쇠 (Img_Lock)")]
    [SerializeField] RectTransform _lock;
    [Tooltip("팡 하는 순간 켤 반짝임(선택). 비어 있으면 건너뛴다.")]
    [SerializeField] GameObject _sparkle;

    [Header("떨림")]
    [SerializeField] float _shakeDuration = 0.45f;
    [Tooltip("좌우로 흔들리는 최대 각도(도)")]
    [SerializeField] float _shakeAngle = 9f;
    [Tooltip("떨림 왕복 횟수")]
    [SerializeField] float _shakeCycles = 5f;

    [Header("팡")]
    [SerializeField] float _popDuration = 0.3f;
    [Tooltip("자물쇠 스케일 배율 (시간 0~1 → 배율)")]
    [SerializeField] AnimationCurve _popScale = new(new Keyframe(0f, 1f), new Keyframe(0.5f, 1.35f), new Keyframe(1f, 1.65f));
    [Tooltip("오버레이 알파 (시간 0~1 → 알파)")]
    [SerializeField] AnimationCurve _popAlpha = new(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.9f), new Keyframe(1f, 0f));

    Quaternion _restRotation;
    Vector3 _restScale;
    bool _cached;

    CancellationTokenSource _cts;
    // 실행 세대. 재진입 시 증가해 **이전 실행이 종료 처리를 하지 못하게** 막는다 — 취소된 실행의
    // finally가 그대로 돌면 새 실행이 켜 둔 오버레이를 즉시 꺼 버린다(`VfxScaleHold`와 같은 축).
    int _generation;

    /// <summary>
    /// 해제 연출이 도는 중인가. 이 창 동안 오버레이는 아직 켜져 있으므로, 소비처가 이걸 보지 않으면
    /// 그 사이 들어온 갱신(자원 변동 등)을 "아직 잠김 → 열림" 전이로 오인해 연출을 다시 건다.
    /// </summary>
    public bool IsPlaying => _cts != null;

    /// <summary>연출 없이 즉시 잠긴 모습으로 되돌린다(부트스트랩·세이브 복원 경로).</summary>
    public void SnapToLocked()
    {
        CancelRunning();
        Cache();
        RestoreRest();
        gameObject.SetActive(true);
    }

    /// <summary>연출 없이 즉시 걷는다.</summary>
    public void SnapToUnlocked()
    {
        CancelRunning();
        Cache();
        RestoreRest();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 해제 연출을 재생한다. 이미 재생 중이면 그쪽을 취소하고 처음부터 다시 시작한다.
    /// 버튼 갱신이 연출을 기다릴 이유는 없으므로 호출부는 `.Forget()`으로 던진다.
    /// </summary>
    /// <param name="ct">호출자 수명 토큰. 이 컴포넌트의 파괴 토큰과 합쳐진다.</param>
    public async UniTask PlayAsync(CancellationToken ct = default)
    {
        Cache();
        CancelRunning();

        int generation = ++_generation;
        gameObject.SetActive(true);
        RestoreRest();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken, ct);
        _cts = cts;
        CancellationToken token = cts.Token;

        try
        {
            // 보이지 않는 동안 연출을 태우지 않는다. 밤→낮 전환의 비활성 구간(같은 프레임 안)을
            // 이 한 줄이 흡수하므로, 어느 이벤트가 먼저 오든 순서에 의존하지 않는다.
            await UniTask.WaitUntil(() => isActiveAndEnabled, cancellationToken: token);

            await ShakeAsync(token);

            if (_sparkle != null) _sparkle.SetActive(true);
            await PopAsync(token);
        }
        catch (OperationCanceledException)
        {
            // 파괴·씬 전환·재진입은 정상 경로다. 마무리는 아래 finally가 세대 판정 후 처리한다.
        }
        finally
        {
            cts.Dispose();

            // 내가 아직 현재 실행일 때만 마무리한다. 재진입으로 밀렸다면 새 실행의 상태를 건드리지 않는다.
            if (_generation == generation)
            {
                _cts = null;
                // 파괴로 취소된 경우 Transform 접근이 예외가 된다(Unity의 가짜 null).
                if (this != null)
                {
                    RestoreRest();
                    // 어느 경로로 끝나든 해금된 타워다 — 자물쇠가 남지 않게 걷는다.
                    gameObject.SetActive(false);
                }
            }
        }
    }

    void Awake() => Cache();

    // 떨림 — 회전만 흔든다. 위치를 흔들면 슬롯 경계를 넘어 옆 칸을 침범한다.
    async UniTask ShakeAsync(CancellationToken token)
    {
        if (_lock == null || _shakeDuration <= 0f) return;

        for (float t = 0f; t < _shakeDuration; t += Time.deltaTime)
        {
            float k = t / _shakeDuration;
            // 뒤로 갈수록 세지게 — "버티다 터진다"로 읽힌다.
            float angle = Mathf.Sin(k * _shakeCycles * Mathf.PI * 2f) * _shakeAngle * k;
            _lock.localRotation = _restRotation * Quaternion.Euler(0f, 0f, angle);
            await UniTask.Yield(token);
        }
        _lock.localRotation = _restRotation;
    }

    // 팡 — 자물쇠가 커지는 동안 오버레이 전체가 걷힌다.
    async UniTask PopAsync(CancellationToken token)
    {
        if (_popDuration <= 0f) return;

        for (float t = 0f; t < _popDuration; t += Time.deltaTime)
        {
            float k = t / _popDuration;
            if (_lock != null) _lock.localScale = _restScale * _popScale.Evaluate(k);
            if (_group != null) _group.alpha = _popAlpha.Evaluate(k);
            await UniTask.Yield(token);
        }
    }

    void CancelRunning()
    {
        if (_cts == null) return;
        _cts.Cancel();
        _cts = null;   // Dispose는 그 실행을 소유한 PlayAsync의 finally가 한다
    }

    void Cache()
    {
        if (_cached) return;
        if (_group == null) _group = GetComponent<CanvasGroup>();
        if (_lock == null)
        {
            var t = transform.Find("Img_Lock");
            if (t != null) _lock = (RectTransform)t;
        }
        if (_lock != null)
        {
            _restRotation = _lock.localRotation;
            _restScale = _lock.localScale;
        }
        _cached = true;
    }

    void RestoreRest()
    {
        if (_lock != null)
        {
            _lock.localRotation = _restRotation;
            _lock.localScale = _restScale;
        }
        if (_group != null) _group.alpha = 1f;
        if (_sparkle != null) _sparkle.SetActive(false);
    }
}
