using System;
using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;

public class FlyingMonsterMove : MonoBehaviour, IRouteMovementAgent
{
    [SerializeField]
    private float arriveDistance = 0.05f;

    [Header("Flying Movement")]
    [SerializeField, Min(0f)]
    private float altitude = 4f;

    [SerializeField, Min(1)]
    private int waypointStep = 5;

    [SerializeField, Min(0f)]
    private float turnSpeed = 6f;

    [Header("Move Speed")]
    [SerializeField]
    private float fallbackMoveSpeed = 3f;

    [SerializeField]
    private float minMoveSpeed = 0.15f;

    private readonly List<Vector3> route = new List<Vector3>();

    private int currentRouteIndex;
    private bool canMove = true;
    private bool routeCompleted;
    private bool hasRoute;

    private MoveSpeedComposer speedComposer;

    // 스턴 축은 속도 컴포저와 독립이다(StunGate 주석 참조).
    private readonly StunGate stunGate = new StunGate();

    // 잔여 경로 거리 계산(#387). 공중 경로는 샘플링·고도 적용을 거친 뒤의 `route`가 실제 비행 경로이므로
    // 그 목록을 그대로 잰다 — 지상 원본 경로로 재면 실제로 나는 거리와 어긋난다.
    private readonly RouteDistanceTracker routeDistance = new RouteDistanceTracker();

    public MovementMode SupportedMode => MovementMode.Flying;
    public bool HasRouteRemaining => currentRouteIndex < route.Count;

    public float RemainingRouteDistance => routeDistance.Remaining(transform.position, route, currentRouteIndex);

    public bool CanMove => canMove;

    public bool IsStopped { get; set; }

    public float EffectiveMoveSpeed => SpeedComposer.EffectiveMoveSpeed;

    public float PatternSpeedFactor
    {
        get => SpeedComposer.PatternSpeedFactor;
        set => SpeedComposer.PatternSpeedFactor = value;
    }

    public event Action RouteCompleted;

    private MoveSpeedComposer SpeedComposer
    {
        get
        {
            speedComposer ??= new MoveSpeedComposer(fallbackMoveSpeed,minMoveSpeed);

            return speedComposer;
        }
    }

    private void Awake()
    {
        // Enemy와 이동 컴포넌트의 Awake 실행 순서와 관계없이
        // 속도 컴포저가 사용할 준비가 되도록 초기화한다.
        _ = SpeedComposer;
    }

    public void SetMoveSpeed(float value)
    {
        bool usedFallback =SpeedComposer.SetBaseMoveSpeed(value);

        if (usedFallback)
        {
            Debug.LogWarning($"[{name}] 유효한 MoveSpeed가 없어 폴백값 {fallbackMoveSpeed}을 사용합니다.",this);
        }
    }

    public void AddSpeedDebuff(int sourceId, float factor)
    {
        SpeedComposer.AddSpeedDebuff(sourceId, factor);
    }

    public void RemoveSpeedDebuff(int sourceId)
    {
        SpeedComposer.RemoveSpeedDebuff(sourceId);
    }

    // ── 스턴 축(IMovementAgent 계약) ─────────────────────────────

    public bool IsStunned => stunGate.IsStunned;

    public void AddStun(int sourceId) => stunGate.Add(sourceId);

    public void RemoveStun(int sourceId) => stunGate.Remove(sourceId);

    public void SetRoute(IReadOnlyList<Vector3> routePoints)
    {
        route.Clear();
        currentRouteIndex = 0;
        routeCompleted = false;

        if (routePoints == null || routePoints.Count == 0)
        {
            hasRoute = false;

            routeDistance.SetRoute(null);   // 이전 경로의 누적이 남아 잔여 거리를 거짓 보고하지 않게 한다

            Debug.LogError($"[{name}] 공중 이동 경로가 비어 있습니다.",this);

            return;
        }

        int safeWaypointStep = Mathf.Max(1, waypointStep);
        int lastAddedIndex = -1;

        // 지상 경로의 모든 지점을 따라가지 않고 일정 간격으로 샘플링한다.
        // 선택한 지점 사이를 직선으로 이동하여 경로 모서리를 가로지른다.
        for (int i = 0; i < routePoints.Count; i += safeWaypointStep)
        {
            route.Add(ApplyAltitude(routePoints[i]));
            lastAddedIndex = i;
        }

        // 마지막 지점은 샘플링 간격과 관계없이 반드시 포함한다.
        int finalIndex = routePoints.Count - 1;

        if (lastAddedIndex != finalIndex)
        {
            route.Add(ApplyAltitude(routePoints[finalIndex]));
        }

        hasRoute = route.Count > 0;

        routeDistance.SetRoute(route);

        if (!hasRoute)
        {
            return;
        }

        // 지상에서 생성된 뒤 상승하지 않고 처음부터 비행 고도에서 시작한다.
        transform.position = route[0];

        SkipReachedPoints();
    }

    private Vector3 ApplyAltitude(Vector3 point)
    {
        point.y += altitude;
        return point;
    }

    private void Update()
    {
        // IsStunned를 IsStopped와 별개 게이트로 두는 이유: IsStopped는 Enemy.Update가 매 프레임
        // 덮어쓰는 값이라 스턴이 여기에 얹히면 1프레임 만에 지워진다.
        if (!hasRoute || IsStopped || IsStunned || routeCompleted)
        {
            return;
        }

        if (currentRouteIndex >= route.Count)
        {
            CompleteRoute();
            return;
        }

        if (!canMove)
        {
            return;
        }

        Vector3 targetPosition = route[currentRouteIndex];
        Vector3 direction = targetPosition - transform.position;

        RotateTowards(direction);

        transform.position = Vector3.MoveTowards(transform.position,targetPosition,EffectiveMoveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position,targetPosition) <= arriveDistance)
        {
            currentRouteIndex++;
            SkipReachedPoints();
        }
    }

    private void RotateTowards(Vector3 direction)
    {
        // 고도는 유지하면서 수평 이동 방향으로만 회전한다.
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction);

        transform.rotation = Quaternion.Slerp(transform.rotation,targetRotation,turnSpeed * Time.deltaTime);
    }

    private void SkipReachedPoints()
    {
        while (currentRouteIndex < route.Count &&Vector3.Distance(transform.position,route[currentRouteIndex]) <= arriveDistance
        )
        {
            currentRouteIndex++;
        }
    }

    private void CompleteRoute()
    {
        if (routeCompleted)
        {
            return;
        }

        routeCompleted = true;
        RouteCompleted?.Invoke();
    }

    public void SetMoveEnabled(bool enabled)
    {
        canMove = enabled;
    }
}
