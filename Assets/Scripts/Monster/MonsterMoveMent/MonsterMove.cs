using System.Collections.Generic;
using UnityEngine;

public class MonsterMove : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float arriveDistance = 0.05f;

    private readonly List<Vector3> route = new List<Vector3>();
    private int currentRouteIndex;

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
        if (currentRouteIndex >= route.Count)
        {
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
