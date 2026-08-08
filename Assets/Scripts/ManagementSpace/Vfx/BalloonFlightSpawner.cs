using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 열기구 항로의 스포너(#138). 씬의 <c>FlightRoute</c>에 붙여 스폰/출발/종료 지점을 배선한다.<br/>
/// <br/>
/// 흐름: 스폰 지점에서 등장 → 출발 지점까지 상승 → 종료 지점까지 순항 → 회수(풀 반환).
/// <b>다음 스폰 타이머는 열기구가 출발 지점에 도달한 순간부터</b> 흐르므로, 상승 중인 열기구는 항상 한 대다.<br/>
/// <br/>
/// <b>스폰 지점이 화면에 없으면 아무것도 만들지 않는다.</b> 아무도 보지 않는 곳에서 열기구가 뜨고 지는 것은
/// 전부 순수한 낭비이고, 플레이어가 그 건물을 볼 때만 비용이 발생하는 편이 상한을 예측 가능하게 만든다.
/// 이미 날고 있는 열기구는 이 게이트와 무관하게 계속 비행하다가 각자 회수된다
/// (<see cref="BalloonFlight"/>의 화면 밖 유예).
/// </summary>
public class BalloonFlightSpawner : MonoBehaviour
{
    [Header("항로")]
    [Tooltip("열기구가 등장하는 지점. 건물에 가려지는 위치가 좋다 — 허공에서 튀어나오는 것이 보이지 않는다.")]
    [SerializeField] Transform _spawnPoint;

    [Tooltip("상승이 끝나고 순항이 시작되는 지점. 다음 스폰 타이머도 여기 도달 시점부터 흐른다.")]
    [SerializeField] Transform _startPoint;

    [Tooltip("순항의 종점. 실제로는 대부분 그 전에 화면 밖 유예로 회수되므로 하드 백스톱에 가깝다.")]
    [SerializeField] Transform _endPoint;

    [Header("열기구")]
    [Tooltip("이 중에서 한 대를 무작위로 고른다. BalloonFlight 컴포넌트가 붙어 있어야 한다.")]
    [SerializeField] GameObject[] _balloonPrefabs;

    [Tooltip("크기 난수 배율. 같은 프리팹이라도 대마다 조금씩 달라 보이게 한다.")]
    [SerializeField] Vector2 _scaleJitter = new Vector2(0.9f, 1.1f);

    [Header("타이밍")]
    [Tooltip("출발 지점 도달 후 다음 스폰까지의 간격(초) 난수 범위. " +
             "고정값으로 두면 열기구가 등간격 행렬처럼 늘어서 인공적으로 읽힌다.")]
    [SerializeField] Vector2 _intervalRange = new Vector2(4f, 7f);

    [Tooltip("상승 속도(units/초).")]
    [SerializeField] float _riseSpeed = 8f;

    [Tooltip("순항 속도(units/초) 난수 범위. 동시 생존 수 ≈ (순항 거리 / 속도) / 간격 이므로 " +
             "이 값이 성능의 유일한 조절 손잡이다.")]
    [SerializeField] Vector2 _cruiseSpeedRange = new Vector2(20f, 30f);

    [Header("가시성 게이트")]
    [Tooltip("스폰 지점 주변 이 반경이 화면에 걸쳐야 스폰한다. 건물 크기 정도로 넉넉히 잡아 " +
             "화면 가장자리에서 스폰이 깜빡이지 않게 한다.")]
    [SerializeField] float _spawnVisibilityRadius = 80f;

    // 프리팹별 비활성 인스턴스 보관소. 열기구마다 난수 속도라 서로 추월할 수 있어 도착 순서가 보장되지 않으므로
    // 큐가 아니라 스택으로 둔다(순서에 의미가 없다).
    private readonly Dictionary<GameObject, Stack<BalloonFlight>> _pool = new Dictionary<GameObject, Stack<BalloonFlight>>();

    // 회수 시 어느 프리팹의 보관소로 돌려보낼지. BalloonFlight가 풀의 존재를 모르게 하려고 스포너가 들고 있는다.
    private readonly Dictionary<BalloonFlight, GameObject> _origin = new Dictionary<BalloonFlight, GameObject>();

    private BalloonFlight _rising;
    private float _timer;

    private void Awake()
    {
        bool missing = _spawnPoint == null || _startPoint == null || _endPoint == null;

        if (missing || _balloonPrefabs == null || _balloonPrefabs.Length == 0)
        {
            Debug.LogError("[열기구] 항로 지점 또는 열기구 프리팹이 배선되지 않아 비활성화합니다.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        // 아무도 안 보고 있으면 타이머조차 흘리지 않는다. 그래야 플레이어가 시선을 돌린 직후
        // (남은 시간이 0이 되어 있어) 곧바로 한 대가 떠올라 "볼 때마다 조용하다"가 되지 않는다.
        if (!CameraVisibility.IsVisible(_spawnPoint.position, _spawnVisibilityRadius))
        {
            return;
        }

        // 상승 구간에는 항상 한 대만 둔다(팀 계약: 다음 타이머는 출발 지점 도달부터).
        if (_rising != null)
        {
            return;
        }

        _timer -= Time.deltaTime;

        if (_timer > 0f)
        {
            return;
        }

        Spawn();
    }

    private void Spawn()
    {
        GameObject prefab = _balloonPrefabs[Random.Range(0, _balloonPrefabs.Length)];
        BalloonFlight balloon = Rent(prefab);

        if (balloon == null)
        {
            enabled = false;
            return;
        }

        balloon.transform.localScale = prefab.transform.localScale * Random.Range(_scaleJitter.x, _scaleJitter.y);

        _rising = balloon;

        balloon.Launch(
            _spawnPoint.position,
            _startPoint.position,
            _endPoint.position,
            _riseSpeed,
            Random.Range(_cruiseSpeedRange.x, _cruiseSpeedRange.y),
            HandleReachedStart,
            Release);
    }

    // 출발 지점 도달 = "떠났다". 여기서부터 다음 스폰까지의 간격을 센다.
    private void HandleReachedStart(BalloonFlight balloon)
    {
        if (_rising == balloon)
        {
            _rising = null;
        }

        _timer = Random.Range(_intervalRange.x, _intervalRange.y);
    }

    private BalloonFlight Rent(GameObject prefab)
    {
        if (_pool.TryGetValue(prefab, out Stack<BalloonFlight> stack) && stack.Count > 0)
        {
            BalloonFlight reused = stack.Pop();
            reused.gameObject.SetActive(true);
            return reused;
        }

        GameObject instance = Instantiate(prefab, transform);

        if (!instance.TryGetComponent(out BalloonFlight created))
        {
            Debug.LogError($"[열기구] {prefab.name}에 BalloonFlight가 없습니다.", this);
            Destroy(instance);
            return null;
        }

        _origin[created] = prefab;
        return created;
    }

    private void Release(BalloonFlight balloon)
    {
        if (_rising == balloon)
        {
            _rising = null;
            _timer = Random.Range(_intervalRange.x, _intervalRange.y);
        }

        balloon.gameObject.SetActive(false);

        if (!_origin.TryGetValue(balloon, out GameObject prefab))
        {
            return;
        }

        if (!_pool.TryGetValue(prefab, out Stack<BalloonFlight> stack))
        {
            stack = new Stack<BalloonFlight>();
            _pool.Add(prefab, stack);
        }

        stack.Push(balloon);
    }
}
