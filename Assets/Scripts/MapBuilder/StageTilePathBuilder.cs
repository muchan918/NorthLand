using System.Collections.Generic;
using UnityEngine;

public class StageTilePathBuildResult
{
    public StageWaypoint SelectedEndWaypoint { get; private set; }
    public StageWaypointDirection NextStartDirection { get; private set; }
    public Vector2Int NextStartPoint { get; private set; }
    public Vector2Int FinalCenterPoint { get; private set; }

    public void Set(
        StageWaypoint selectedEndWaypoint,
        StageWaypointDirection nextStartDirection,
        Vector2Int nextStartPoint)
    {
        SelectedEndWaypoint = selectedEndWaypoint;
        NextStartDirection = nextStartDirection;
        NextStartPoint = nextStartPoint;
        FinalCenterPoint = Vector2Int.zero;
    }

    public void SetFinalCenterPoint(Vector2Int finalCenterPoint)
    {
        SelectedEndWaypoint = null;
        NextStartDirection = default;
        NextStartPoint = Vector2Int.zero;
        FinalCenterPoint = finalCenterPoint;
    }
}

public class StageTilePathBuilder
{
    private readonly PathGenerator pathGenerator;
    private readonly StageConnectionManager connectionManager;
    private readonly Vector2Int[] centerPoints;
    private readonly int mapSize;
    private readonly int maxPathGenerateTryCount;

    public StageTilePathBuilder(
        PathGenerator pathGenerator,
        StageConnectionManager connectionManager,
        Vector2Int[] centerPoints,
        int mapSize,
        int maxPathGenerateTryCount)
    {
        this.pathGenerator = pathGenerator;
        this.connectionManager = connectionManager;
        this.centerPoints = centerPoints;
        this.mapSize = mapSize;
        this.maxPathGenerateTryCount = maxPathGenerateTryCount;
    }

    public bool TryBuild(
        Vector2Int currentMapOffset,
        Vector2Int nextMapOffset,
        Vector2Int currentStartPoint,
        StageWaypointDirection currentStartDirection,
        List<Vector2Int> occupiedMapOffsets,
        IReadOnlyCollection<Vector2Int> generatedRoadWorldPoints,
        List<Vector2Int> path,
        StageTilePathBuildResult result)
    {
        StageWaypointDirection nextDirection = GetDirectionToNextMap(currentMapOffset, nextMapOffset);

        for (int i = 0; i < maxPathGenerateTryCount; i++)
        {
            path.Clear();

            if (!connectionManager.TryGetEndPointByDirection(
                    currentStartDirection,
                    nextDirection,
                    currentMapOffset,
                    occupiedMapOffsets,
                    out StageWaypoint endWaypoint))
            {
                break;
            }

            Vector2Int centerPoint = centerPoints[Random.Range(0, centerPoints.Length)];

            if (pathGenerator.TryGeneratePath(
                    currentStartPoint,
                    centerPoint,
                    endWaypoint.point,
                    currentMapOffset,
                    mapSize,
                    generatedRoadWorldPoints,
                    path))
            {
                result.Set(
                    endWaypoint,
                    connectionManager.GetOppositeDirection(endWaypoint.direction),
                    connectionManager.GetOppositeStartPoint(endWaypoint));

                return true;
            }
        }

        path.Clear();
        return false;
    }

    public bool TryBuildToCenter(
        Vector2Int currentMapOffset,
        Vector2Int currentStartPoint,
        IReadOnlyCollection<Vector2Int> generatedRoadWorldPoints,
        List<Vector2Int> path,
        StageTilePathBuildResult result)
    {
        for (int i = 0; i < maxPathGenerateTryCount; i++)
        {
            path.Clear();
            Vector2Int centerPoint = centerPoints[Random.Range(0, centerPoints.Length)];

            if (pathGenerator.TryGeneratePath(
                    currentStartPoint,
                    centerPoint,
                    centerPoint,
                    currentMapOffset,
                    mapSize,
                    generatedRoadWorldPoints,
                    path))
            {
                result.SetFinalCenterPoint(centerPoint);
                return true;
            }
        }

        path.Clear();
        return false;
    }

    private StageWaypointDirection GetDirectionToNextMap(Vector2Int currentOffset, Vector2Int nextOffset)
    {
        Vector2Int delta = nextOffset - currentOffset;

        if (delta == Vector2Int.down)
        {
            return StageWaypointDirection.Top;
        }

        if (delta == Vector2Int.up)
        {
            return StageWaypointDirection.Bottom;
        }

        if (delta == Vector2Int.left)
        {
            return StageWaypointDirection.Left;
        }

        return StageWaypointDirection.Right;
    }
}