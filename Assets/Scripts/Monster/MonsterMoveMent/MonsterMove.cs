using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

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
            return;
        }

        Vector3 targetPosition = route[currentRouteIndex];
        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            moveSpeed * Time.deltaTime
        );


        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }


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
