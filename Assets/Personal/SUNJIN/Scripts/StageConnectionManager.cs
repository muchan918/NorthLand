using System.Collections.Generic;
using UnityEngine;

public class StageConnectionManager
{
    private readonly int mapSize;
    private readonly List<StageWaypoint> waypoints;

    public StageConnectionManager(int mapSize, List<StageWaypoint> waypoints)
    {
        this.mapSize = mapSize;
        this.waypoints = waypoints;
    }

    public bool TryGetRandomEndPoint(
        StageWaypointDirection startDirection,
        Vector2Int mapOffset,
        List<Vector2Int> occupiedMapOffsets,
        out StageWaypoint endWaypoint)
    {
        List<StageWaypoint> candidates = new List<StageWaypoint>();

        foreach (StageWaypoint waypoint in waypoints)
        {
            if (waypoint.direction == startDirection)
            {
                continue;
            }

            Vector2Int nextMapOffset = GetNextMapOffset(mapOffset, waypoint.direction);

            if (occupiedMapOffsets.Contains(nextMapOffset))
            {
                continue;
            }

            candidates.Add(waypoint);
        }

        if (candidates.Count == 0)
        {
            endWaypoint = null;
            return false;
        }

        endWaypoint = candidates[Random.Range(0, candidates.Count)];
        return true;
    }

    public Vector2Int GetNextMapOffset(Vector2Int mapOffset, StageWaypointDirection direction)
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

    public Vector2Int GetOppositeStartPoint(StageWaypoint endWaypoint)
    {
        StageWaypointDirection oppositeDirection = GetOppositeDirection(endWaypoint.direction);

        foreach (StageWaypoint waypoint in waypoints)
        {
            if (waypoint.direction == oppositeDirection && IsAlignedWaypoint(endWaypoint, waypoint))
            {
                return waypoint.point;
            }
        }

        Debug.LogWarning($"연결되는 반대편 Waypoint를 찾지 못했습니다. {endWaypoint.direction} {endWaypoint.point}");
        return GetMirroredPoint(endWaypoint.point, endWaypoint.direction);
    }

    public StageWaypointDirection GetOppositeDirection(StageWaypointDirection direction)
    {
        switch (direction)
        {
            case StageWaypointDirection.Top:
                return StageWaypointDirection.Bottom;
            case StageWaypointDirection.Bottom:
                return StageWaypointDirection.Top;
            case StageWaypointDirection.Left:
                return StageWaypointDirection.Right;
            case StageWaypointDirection.Right:
                return StageWaypointDirection.Left;
            default:
                return direction;
        }
    }

    private bool IsAlignedWaypoint(StageWaypoint exit, StageWaypoint entrance)
    {
        if (exit.direction == StageWaypointDirection.Top ||
            exit.direction == StageWaypointDirection.Bottom)
        {
            return exit.point.x == entrance.point.x;
        }

        return exit.point.y == entrance.point.y;
    }

    private Vector2Int GetMirroredPoint(Vector2Int point, StageWaypointDirection direction)
    {
        switch (direction)
        {
            case StageWaypointDirection.Top:
                return new Vector2Int(point.x, mapSize - 1);
            case StageWaypointDirection.Bottom:
                return new Vector2Int(point.x, 0);
            case StageWaypointDirection.Left:
                return new Vector2Int(mapSize - 1, point.y);
            case StageWaypointDirection.Right:
                return new Vector2Int(0, point.y);
            default:
                return point;
        }
    }
}