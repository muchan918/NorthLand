using System.Collections.Generic;
using UnityEngine;

public class StageRoadTracker
{
    private readonly int mapSize;
    private readonly HashSet<Vector2Int> roadWorldPoints = new HashSet<Vector2Int>();

    public IReadOnlyCollection<Vector2Int> RoadWorldPoints => roadWorldPoints;

    public StageRoadTracker(int mapSize)
    {
        this.mapSize = mapSize;
    }

    public void AddPath(Vector2Int mapOffset, IEnumerable<Vector2Int> path)
    {
        foreach (Vector2Int pathPoint in path)
        {
            roadWorldPoints.Add(ToWorldPoint(pathPoint, mapOffset));
        }
    }

    public void Clear()
    {
        roadWorldPoints.Clear();
    }

    private Vector2Int ToWorldPoint(Vector2Int localPoint, Vector2Int mapOffset)
    {
        return new Vector2Int(
            localPoint.x + mapOffset.x * mapSize,
            localPoint.y + mapOffset.y * mapSize);
    }
}