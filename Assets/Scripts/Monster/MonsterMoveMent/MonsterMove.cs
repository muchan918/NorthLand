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
            moveSpeed * Time.deltaTime
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
