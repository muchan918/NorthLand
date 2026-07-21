using NorthLand.Combat;
using System.Collections.Generic;
using UnityEngine;
using static EnemyAsset;

public class MonsterMove : MonoBehaviour, IMovementAgent
{

    [SerializeField] private EnemyAsset enemyAsset;

    [SerializeField] private float arriveDistance = 0.05f;
    private float moveSpeed;
    private bool canMove = true;

    private readonly List<Vector3> route = new List<Vector3>();
    private int currentRouteIndex;

    public bool HasRouteRemaining => currentRouteIndex < route.Count;
    public bool CanMove => canMove;
    public bool IsStopped { get; set; }

    private void Awake()
    {
        ApplyMoveSpeed();
    }


    public void ApplyMoveSpeed()
    {
        if (enemyAsset == null)
        {
            Debug.LogError("EnemyAsset이 지정되지 않았습니다.", this);
            return;
        }

        EnemyAsset.CombatFields stat = GetCombatStat();

        if (stat == null)
        {
            Debug.LogError(
                $"{enemyAsset.EnemyType} 타입의 전투 스탯이 지정되지 않았습니다.",
                enemyAsset
            );
            return;
        }

        moveSpeed = stat.MoveSpeed;
    }

    private EnemyAsset.CombatFields GetCombatStat()
    {
        return enemyAsset.EnemyType switch
        {
            EnemyType.Melee => enemyAsset.Melee?.Stat,
            EnemyType.Ranged => enemyAsset.Ranged?.Stat,
            EnemyType.Boss => enemyAsset.Boss?.Stat,
            _ => null
        };
    }


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
        if (IsStopped)
        {
            return;
        }

        if (currentRouteIndex >= route.Count)
        {
            Destroy(gameObject);
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
