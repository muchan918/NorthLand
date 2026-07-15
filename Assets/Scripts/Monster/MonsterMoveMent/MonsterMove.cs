using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;

public class MonsterMove : MonoBehaviour, IMovementAgent
{
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float arriveDistance = 0.05f;

    private readonly List<Vector3> route = new List<Vector3>();
    private int currentRouteIndex;

    // 전투 AI(Enemy)가 제어하는 정지 플래그. NavMeshAgent.isStopped와 같은 역할이며,
    // 이 컴포넌트는 전투를 모른다 — 지시받은 대로 멈추거나 전진할 뿐이다.
    public bool IsStopped { get; set; }

    public void SetRoute(List<Vector3> routePoints)
    {
        route.Clear();
        currentRouteIndex = 0;

        if (routePoints == null)
        {
            return;
        }

        route.AddRange(routePoints);
        SkipReachedPoints();
    }

    private void Update()
    {
        // 전투 AI가 멈추라고 지시하면(사거리 내 대상 존재 등) 전진하지 않는다.
        if (IsStopped)
        {
            return;
        }

        if (currentRouteIndex >= route.Count)
        {
            // 경로 끝(본진) 도달 → 디스폰. 스포너의 웨이브 클리어 판정(살아있는 몬스터 수)에서 빠진다.
            // 본진 데미지는 전투 통합 시 별도 처리(TODO) — 지금은 디스폰만.
            Destroy(gameObject);
            return;
        }

        Vector3 targetPosition = route[currentRouteIndex];
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
}
