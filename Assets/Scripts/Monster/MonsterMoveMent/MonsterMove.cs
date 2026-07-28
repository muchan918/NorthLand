using System;
using NorthLand.Combat;
using System.Collections.Generic;
using UnityEngine;

public class MonsterMove : MonoBehaviour, IRouteMovementAgent
{


    [SerializeField] private float arriveDistance = 0.05f;

    private bool canMove = true;

    private readonly List<Vector3> route = new List<Vector3>();
    private int currentRouteIndex;

    public bool HasRouteRemaining => currentRouteIndex < route.Count;
    public bool CanMove => canMove;
    public bool IsStopped { get; set; }

    [SerializeField] private float fallbackMoveSpeed = 3f;

    // 이동속도 다축 합성의 하한(#233). 패턴 배수와 디버프 배수가 곱해져 0에 수렴해도
    // 이 값 아래로는 내려가지 않는다. 완전 정지는 속도 축이 아니라 IsStopped로만 표현한다 —
    // 감속으로 몬스터를 영구 정지시켜 웨이브를 소프트락하는 경로를 막기 위함이다.
    [SerializeField] private float minMoveSpeed = 0.15f;

    // 기준 이동속도(Enemy가 Stat.MoveSpeed로 주입). 배수가 곱해지기 전의 값이다.
    private float baseMoveSpeed;
    private bool hasInjectedMoveSpeed;

    // 패턴 축 — BT 노드가 소유(돌진 가속 / 방어 태세 크롤).
    private float patternSpeedFactor = 1f;

    // 디버프 축 — 소스별 곱산 중첩(이동속도 감소 타워 등). product는 캐시다.
    private readonly Dictionary<int, float> speedDebuffs = new Dictionary<int, float>();
    private float speedDebuffProduct = 1f;

    // 합성 결과. Update가 매 프레임 곱셈을 다시 하지 않도록 축이 바뀔 때만 재계산한다.
    private float effectiveMoveSpeed;

    public event Action RouteCompleted;

    private bool routeCompleted;
    private bool hasRoute;

    private void Awake()
    {
        if (!hasInjectedMoveSpeed)
        {
            baseMoveSpeed = fallbackMoveSpeed;
        }

        RecomputeEffectiveMoveSpeed();
    }

    // 기준 이동속도를 주입한다(배수 축은 건드리지 않는다).
    // 0 이하는 데이터 오류로 보고 폴백을 쓴다 — 배수가 0에 수렴하는 경우는 minMoveSpeed가 받아내므로
    // 이 폴백 경로에 걸리지 않는다(#233 이전에는 크롤 배수가 여기 걸려 오히려 빨라졌다).
    public void SetMoveSpeed(float value)
    {
        if (value > 0f)
        {
            baseMoveSpeed = value;
        }
        else
        {
            baseMoveSpeed = Mathf.Max(0.01f, fallbackMoveSpeed);

            Debug.LogWarning($"[{name}] 유효한 MoveSpeed가 없어 폴백값 {baseMoveSpeed}을 사용합니다.",this);
        }

        hasInjectedMoveSpeed = true;

        RecomputeEffectiveMoveSpeed();
    }

    // ── 이동속도 다축 합성(IMovementAgent 계약) ─────────────────────────────

    public float EffectiveMoveSpeed => effectiveMoveSpeed;

    public float PatternSpeedFactor
    {
        get => patternSpeedFactor;
        set
        {
            patternSpeedFactor = Mathf.Max(0f, value);

            RecomputeEffectiveMoveSpeed();
        }
    }


    public void AddSpeedDebuff(int sourceId, float factor)
    {
        speedDebuffs[sourceId] = Mathf.Max(0f, factor);

        RecomputeSpeedDebuffProduct();
    }

    public void RemoveSpeedDebuff(int sourceId)
    {
        if (speedDebuffs.Remove(sourceId))
        {
            RecomputeSpeedDebuffProduct();
        }
    }

    private void RecomputeSpeedDebuffProduct()
    {
        speedDebuffProduct = 1f;

        foreach (float factor in speedDebuffs.Values)
        {
            speedDebuffProduct *= factor;
        }

        RecomputeEffectiveMoveSpeed();
    }

    private void RecomputeEffectiveMoveSpeed()
    {
        effectiveMoveSpeed = Mathf.Max(
            minMoveSpeed,
            baseMoveSpeed * patternSpeedFactor * speedDebuffProduct
        );
    }


    public void SetRoute(IReadOnlyList<Vector3> routePoints)
    {
        route.Clear();
        currentRouteIndex = 0;
        routeCompleted = false;
        hasRoute = routePoints != null && routePoints.Count > 0;

        if (!hasRoute)
        {
            return;
        }

        route.AddRange(routePoints);
        SkipReachedPoints();
    }

    private void Update()
    {
        if (!hasRoute || IsStopped || routeCompleted)
        {
            return;
        }

        if (currentRouteIndex >= route.Count)
        {
            routeCompleted = true;
            RouteCompleted?.Invoke();
            return;
        }

        if (!canMove)
        {
            return;
        }

        Vector3 targetPosition = route[currentRouteIndex];

        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            effectiveMoveSpeed * Time.deltaTime
        );

        if (Vector3.Distance(transform.position, targetPosition) <= arriveDistance)
        {
            currentRouteIndex++;
            SkipReachedPoints();
        }
    }

    private void SkipReachedPoints()
    {
        while (currentRouteIndex < route.Count && Vector3.Distance(transform.position, route[currentRouteIndex]) <= arriveDistance)
        {
            currentRouteIndex++;
        }
    }

    public void SetMoveEnabled(bool enabled)
    {
        canMove = enabled;
    }


}
