using System;
using NorthLand.Combat;
using System.Collections.Generic;
using UnityEngine;

public class MonsterMove : MonoBehaviour, IRouteMovementAgent
{


    [SerializeField] private float arriveDistance = 0.05f;

    [SerializeField] private float turnSpeed = 720f;
    private bool canMove = true;

    private readonly List<Vector3> route = new List<Vector3>();
    private int currentRouteIndex;

    // 잔여 경로 거리 계산(#387). 경로 확정 시 누적을 한 번 만들어 두고 조회는 O(1)로 답한다.
    private readonly RouteDistanceTracker routeDistance = new RouteDistanceTracker();

    public MovementMode SupportedMode => MovementMode.Ground;
    public bool HasRouteRemaining => currentRouteIndex < route.Count;

    public float RemainingRouteDistance => routeDistance.Remaining(transform.position, route, currentRouteIndex);
    public bool CanMove => canMove;
    public bool IsStopped { get; set; }

    [SerializeField] private float fallbackMoveSpeed = 3f;

    // 이동속도 다축 합성의 하한(#233). 패턴 배수와 디버프 배수가 곱해져 0에 수렴해도
    // 이 값 아래로는 내려가지 않는다. 완전 정지는 속도 축이 아니라 IsStopped로만 표현한다 —
    // 감속으로 몬스터를 영구 정지시켜 웨이브를 소프트락하는 경로를 막기 위함이다.
    [SerializeField] private float minMoveSpeed = 0.15f;

    public event Action RouteCompleted;

    private bool routeCompleted;
    private bool hasRoute;

    private MoveSpeedComposer speedComposer;

    // 스턴 축은 속도 컴포저와 독립이다(StunGate 주석 참조).
    private readonly StunGate stunGate = new StunGate();

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
        _ = SpeedComposer;
    }


    // 기준 이동속도를 주입한다(배수 축은 건드리지 않는다).
    // 0 이하는 데이터 오류로 보고 폴백을 쓴다 — 배수가 0에 수렴하는 경우는 minMoveSpeed가 받아내므로
    // 이 폴백 경로에 걸리지 않는다(#233 이전에는 크롤 배수가 여기 걸려 오히려 빨라졌다).
  


    public void SetMoveSpeed(float value)
    {
        bool usedFallback = SpeedComposer.SetBaseMoveSpeed(value);

        if (usedFallback)
        {
            Debug.LogWarning(
                $"[{name}] 유효한 MoveSpeed가 없어 폴백값 " +
                $"{fallbackMoveSpeed}을 사용합니다.",
                this
            );
        }
    }

 



    // ── 이동속도 다축 합성(IMovementAgent 계약) ─────────────────────────────

    public float EffectiveMoveSpeed => SpeedComposer.EffectiveMoveSpeed;

    public float PatternSpeedFactor
    {
        get => SpeedComposer.PatternSpeedFactor;
        set => SpeedComposer.PatternSpeedFactor = value;
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
        hasRoute = routePoints != null && routePoints.Count > 0;

        if (!hasRoute)
        {
            routeDistance.SetRoute(null);   // 이전 경로의 누적이 남아 잔여 거리를 거짓 보고하지 않게 한다
            return;
        }

        route.AddRange(routePoints);
        routeDistance.SetRoute(route);
        SkipReachedPoints();
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
            routeCompleted = true;
            RouteCompleted?.Invoke();
            return;
        }

        if (!canMove)
        {
            return;
        }

        Vector3 startPosition = transform.position;
        float remainingDistance = EffectiveMoveSpeed * Time.deltaTime;

        while (remainingDistance > 0f && currentRouteIndex < route.Count)
        {
            Vector3 targetPosition = route[currentRouteIndex];
            Vector3 toTarget = targetPosition - transform.position;
            float distance = toTarget.magnitude;

            if (distance <= arriveDistance)
            {
                currentRouteIndex++;
                continue;
            }

            float step = Mathf.Min(remainingDistance, distance);
            transform.position += toTarget / distance * step;
            remainingDistance -= step;

            if (step >= distance)
                currentRouteIndex++;
            else
                break;
        }

        Vector3 moveDirection = transform.position - startPosition;
        moveDirection.y = 0f;

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);

            transform.rotation = Quaternion.RotateTowards(transform.rotation,targetRotation,turnSpeed * Time.deltaTime);
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
