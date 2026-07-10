using System.Collections.Generic;
using UnityEngine;

public class StageMapRouteGenerator
{
    private readonly StageWaypointDirection[] allDirections =
    {
        StageWaypointDirection.Top,
        StageWaypointDirection.Bottom,
        StageWaypointDirection.Left,
        StageWaypointDirection.Right,
    };

    public bool TryGenerateRoute(
        int mapCount,
        int minMapX,
        int maxMapX,
        int minMapY,
        int maxMapY,
        int tryCount,
        List<Vector2Int> route)
    {
        for (int i = 0; i < tryCount; i++)
        {
            route.Clear();
            route.Add(Vector2Int.zero);

            HashSet<Vector2Int> occupied = new HashSet<Vector2Int>
            {
                Vector2Int.zero,
            };

            if (TryFillOffsets(mapCount, minMapX, maxMapX, minMapY, maxMapY, route, occupied))
            {
                return true;
            }
        }

        route.Clear();
        return false;
    }

    private bool TryFillOffsets(
        int mapCount,
        int minMapX,
        int maxMapX,
        int minMapY,
        int maxMapY,
        List<Vector2Int> route,
        HashSet<Vector2Int> occupied)
    {
        while (route.Count < mapCount)
        {
            Vector2Int currentOffset = route[route.Count - 1];
            List<Vector2Int> candidates = GetValidNextOffsets(currentOffset, minMapX, maxMapX, minMapY, maxMapY, occupied);

            if (candidates.Count == 0)
            {
                return false;
            }

            MapRandom.Shuffle(candidates);
            Vector2Int nextOffset = candidates[0];

            route.Add(nextOffset);
            occupied.Add(nextOffset);
        }

        return true;
    }

    private List<Vector2Int> GetValidNextOffsets(
        Vector2Int currentOffset,
        int minMapX,
        int maxMapX,
        int minMapY,
        int maxMapY,
        HashSet<Vector2Int> occupied)
    {
        List<Vector2Int> candidates = new List<Vector2Int>();

        foreach (StageWaypointDirection direction in allDirections)
        {
            Vector2Int nextOffset = GetNextMapOffset(currentOffset, direction);

            if (occupied.Contains(nextOffset))
            {
                continue;
            }

            if (nextOffset.x < minMapX || nextOffset.x > maxMapX)
            {
                continue;
            }

            if (nextOffset.y < minMapY || nextOffset.y > maxMapY)
            {
                continue;
            }

            candidates.Add(nextOffset);
        }

        return candidates;
    }

    private Vector2Int GetNextMapOffset(Vector2Int mapOffset, StageWaypointDirection direction)
    {
        switch (direction)
        {
            case StageWaypointDirection.Top:
                return mapOffset + Vector2Int.down;
            case StageWaypointDirection.Bottom:
                return mapOffset + Vector2Int.up;
            case StageWaypointDirection.Left:
                return mapOffset + Vector2Int.left;
            case StageWaypointDirection.Right:
                return mapOffset + Vector2Int.right;
            default:
                return mapOffset;
        }
    }
}
