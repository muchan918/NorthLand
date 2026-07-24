using System;
using NorthLand.Combat;
using System.Collections.Generic;
using UnityEngine;

public class MonsterMove : MonoBehaviour, IMovementAgent
{


    [SerializeField] private float arriveDistance = 0.05f;

    private bool canMove = true;

    private readonly List<Vector3> route = new List<Vector3>();
    private int currentRouteIndex;

    public bool HasRouteRemaining => currentRouteIndex < route.Count;
    public bool CanMove => canMove;
    public bool IsStopped { get; set; }

    [SerializeField] private float fallbackMoveSpeed = 3f;

    private float moveSpeed;
    private bool hasInjectedMoveSpeed;

    // 슬로우/스턴 배율(#164). 기준 moveSpeed에 곱해진다. 1=정상, 0.6=40%감속, 0=완전정지(스턴).
    // 기준값은 안 건드리고 배율만 조작 → 만료 시 1로 원복하면 그만(눈덩이·유실 없음). StatusEffectHandler가 세팅.
    private float slowMultiplier = 1f;

    public event Action RouteCompleted;

    private bool routeCompleted;
    private bool hasRoute;

    private void Awake()
    {
        if (!hasInjectedMoveSpeed)
        {
            moveSpeed = fallbackMoveSpeed;
        }
    }

    public void SetMoveSpeed(float value)
    {
        if (value > 0f)
        {
            moveSpeed = value;
        }
        else
        {
            moveSpeed = Mathf.Max(0.01f, fallbackMoveSpeed);

            Debug.LogWarning($"[{name}] 유효한 MoveSpeed가 없어 폴백값 {moveSpeed}을 사용합니다.",this);
        }

        hasInjectedMoveSpeed = true;
    }

    // 슬로우/스턴 인프라(#164): 이동속도 배율 설정. 1=정상, 0=정지. StatusEffectHandler가 활성 효과를 합쳐 호출.
    public void SetSlowMultiplier(float value)
    {
        slowMultiplier = Mathf.Clamp01(value);
    }


    public void SetRoute(List<Vector3> routePoints)
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
            moveSpeed * slowMultiplier * Time.deltaTime   // 슬로우/스턴 배율 반영(#164)
        );

        if (Vector3.Distance(transform.position, targetPosition) <= arriveDistance)
        {
            currentRouteIndex++;
            SkipReachedPoints();
        }
    }

    private void SkipReachedPoints()
    {
        while (currentRouteIndex < route.Count &&
               Vector3.Distance(transform.position, route[currentRouteIndex]) <= arriveDistance)
        {
            currentRouteIndex++;
        }
    }

    public void SetMoveEnabled(bool enabled)
    {
        canMove = enabled;
    }
}
