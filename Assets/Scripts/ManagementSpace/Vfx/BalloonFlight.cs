using System;
using UnityEngine;

/// <summary>
/// 열기구 한 대의 비행(#138). 스폰 지점에서 떠올라(Rise) 출발 지점부터 종료 지점까지 순항(Cruise)한다.
/// 스포너가 <see cref="Launch"/>로 경로와 속도를 주입하고, 두 시점(출발 지점 도달 / 비행 종료)을 콜백으로 돌려받는다.<br/>
/// <br/>
/// <b>변주는 전부 여기서 대(臺)마다 난수로 정해진다</b> — 속도·좌우 오프셋·상하 흔들림·회전. 열기구가 늘어도
/// 비용은 프레임당 float 연산 몇 개라 사실상 0이다. 비싼 것은 오브젝트 수와 파티클 시스템 수지 이 계산이 아니다.<br/>
/// <br/>
/// <b>회수 시점</b>은 종료 지점 도달 또는 <see cref="_offScreenGrace"/> 동안 화면 밖에 머무는 것, 둘 중 먼저다.
/// 화면을 벗어나자마자 지우면 플레이어가 카메라를 그쪽으로 돌렸을 때 있어야 할 열기구가 없어 이질감이 생기므로,
/// <b>유예 시간 동안은 계속 날린 채로</b> 둔다.
/// </summary>
public class BalloonFlight : MonoBehaviour
{
    private enum Phase { Idle, Rise, Cruise }

    [Tooltip("순항 중 상하로 흔들리는 진폭(월드 units). 0이면 흔들리지 않는다.")]
    [SerializeField] float _bobAmplitude = 5f;

    [Tooltip("상하 흔들림의 주기(초). 이 범위에서 대마다 난수로 정해진다.")]
    [SerializeField] Vector2 _bobPeriodRange = new Vector2(4f, 8f);

    [Tooltip("천천히 도는 요(yaw) 속도(도/초). 부호는 대마다 난수로 뒤집힌다.")]
    [SerializeField] Vector2 _yawSpeedRange = new Vector2(5f, 15f);

    [Tooltip("직선 경로에서 좌우로 벗어나는 최대 거리(월드 units). 대마다 난수라 전부 같은 레일을 타지 않는다. " +
             "출발·종료 지점에서는 0이고 중간에서 최대가 되므로 완만한 호를 그린다.")]
    [SerializeField] float _lateralOffsetRange = 25f;

    [Tooltip("가시성 판정에 쓰는 반경(월드 units). 열기구 실제 크기보다 넉넉하게 잡아 가장자리에서 깜빡이지 않게 한다.")]
    [SerializeField] float _visibilityRadius = 25f;

    [Tooltip("화면 밖으로 나간 뒤 회수까지 기다리는 시간(초). 이 동안에도 계속 비행하므로, " +
             "플레이어가 카메라를 돌리면 있어야 할 자리에 그대로 있다.")]
    [SerializeField] float _offScreenGrace = 8f;

    private ParticleSystem[] _particles;

    private Phase _phase = Phase.Idle;
    private Vector3 _start;
    private Vector3 _end;
    private Vector3 _lateralAxis;
    private float _cruiseDistance;
    private float _riseSpeed;
    private float _cruiseSpeed;
    private float _traveled;
    private float _cruiseElapsed;
    private float _bobPeriod;
    private float _yawSpeed;
    private float _lateralOffset;
    private float _offScreenElapsed;

    private Action<BalloonFlight> _onReachedStart;
    private Action<BalloonFlight> _onFinished;

    private void Awake()
    {
        _particles = GetComponentsInChildren<ParticleSystem>(true);
    }

    /// <summary>
    /// 비행을 시작한다. 풀에서 꺼내 재사용할 때도 이 진입점 하나만 부르면 상태가 완전히 초기화된다.
    /// </summary>
    public void Launch(Vector3 spawn, Vector3 start, Vector3 end, float riseSpeed, float cruiseSpeed,
        Action<BalloonFlight> onReachedStart, Action<BalloonFlight> onFinished)
    {
        _start = start;
        _end = end;
        _riseSpeed = Mathf.Max(0.01f, riseSpeed);
        _cruiseSpeed = Mathf.Max(0.01f, cruiseSpeed);
        _onReachedStart = onReachedStart;
        _onFinished = onFinished;

        Vector3 flat = end - start;
        flat.y = 0f;
        _cruiseDistance = Mathf.Max(0.01f, Vector3.Distance(start, end));

        // 진행 방향의 수평 수직축 — 여기로 좌우 오프셋을 준다. 경로가 수직에 가까우면 안전한 축으로 대체.
        _lateralAxis = flat.sqrMagnitude > 0.0001f
            ? Vector3.Cross(Vector3.up, flat.normalized)
            : Vector3.right;

        _lateralOffset = UnityEngine.Random.Range(-_lateralOffsetRange, _lateralOffsetRange);
        _bobPeriod = UnityEngine.Random.Range(_bobPeriodRange.x, _bobPeriodRange.y);
        _yawSpeed = UnityEngine.Random.Range(_yawSpeedRange.x, _yawSpeedRange.y) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);

        _traveled = 0f;
        _cruiseElapsed = 0f;
        _offScreenElapsed = 0f;
        _phase = Phase.Rise;

        transform.position = spawn;
        transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

        RestartParticles();
    }

    private void Update()
    {
        if (_phase == Phase.Idle)
        {
            return;
        }

        // 배속·일시정지를 그대로 따른다 — 열기구는 UI 안내가 아니라 월드 배경이다(불꽃 파티클의 Use Unscaled Time = off와 짝).
        float dt = Time.deltaTime;

        if (_phase == Phase.Rise)
        {
            UpdateRise(dt);
            return;
        }

        UpdateCruise(dt);
    }

    private void UpdateRise(float dt)
    {
        transform.position = Vector3.MoveTowards(transform.position, _start, _riseSpeed * dt);

        if (Vector3.SqrMagnitude(transform.position - _start) > 0.01f)
        {
            return;
        }

        _phase = Phase.Cruise;

        // 상승이 끝난 이 시점이 "출발"이다 — 스포너의 다음 스폰 타이머가 여기서 시작된다.
        Action<BalloonFlight> callback = _onReachedStart;
        _onReachedStart = null;
        callback?.Invoke(this);
    }

    private void UpdateCruise(float dt)
    {
        _traveled += _cruiseSpeed * dt;
        _cruiseElapsed += dt;

        float t = Mathf.Clamp01(_traveled / _cruiseDistance);
        Vector3 position = Vector3.Lerp(_start, _end, t);

        // 양 끝에서 0, 중간에서 최대 — 경로에서 벗어났다가 되돌아오는 완만한 호가 된다.
        position += _lateralAxis * (_lateralOffset * Mathf.Sin(t * Mathf.PI));

        // 순항 시작 시점에 0이라 위치가 튀지 않는다(Time.time을 쓰면 시작 위상이 제각각이라 첫 프레임에 점프한다).
        position.y += _bobAmplitude * Mathf.Sin(_cruiseElapsed * (2f * Mathf.PI / Mathf.Max(0.01f, _bobPeriod)));

        transform.position = position;
        transform.Rotate(0f, _yawSpeed * dt, 0f, Space.World);

        if (t >= 1f)
        {
            Finish();
            return;
        }

        UpdateOffScreenGrace(dt);
    }

    private void UpdateOffScreenGrace(float dt)
    {
        if (CameraVisibility.IsVisible(transform.position, _visibilityRadius))
        {
            _offScreenElapsed = 0f;
            return;
        }

        _offScreenElapsed += dt;

        if (_offScreenElapsed >= _offScreenGrace)
        {
            Finish();
        }
    }

    private void Finish()
    {
        _phase = Phase.Idle;

        Action<BalloonFlight> callback = _onFinished;
        _onFinished = null;
        callback?.Invoke(this);
    }

    // 풀에서 꺼낸 오브젝트는 Awake가 다시 돌지 않아 Play On Awake가 발화하지 않는다 —
    // 명시적으로 지우고 다시 틀지 않으면 두 번째 사용부터 불꽃 없는 열기구가 날아다닌다.
    private void RestartParticles()
    {
        if (_particles == null)
        {
            return;
        }

        for (int i = 0; i < _particles.Length; i++)
        {
            ParticleSystem ps = _particles[i];

            if (ps == null)
            {
                continue;
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }
    }
}
