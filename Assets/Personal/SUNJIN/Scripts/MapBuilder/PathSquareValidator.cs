using System.Collections.Generic;
using UnityEngine;

public class PathSquareValidator
{
    public bool WouldMakeSquareWithCurrentPath(
        Vector2Int localPoint,
        Vector2Int currentMapOffset,
        int mapSize,
        IReadOnlyCollection<Vector2Int> generatedRoadWorldPoints,
        List<Vector2Int> path)
    {
        HashSet<Vector2Int> roadWorldPoints = new HashSet<Vector2Int>(generatedRoadWorldPoints);

        foreach (Vector2Int pathPoint in path)
        {
            roadWorldPoints.Add(ToWorldPoint(pathPoint, currentMapOffset, mapSize));
        }

        return WouldMakeSquare(ToWorldPoint(localPoint, currentMapOffset, mapSize), roadWorldPoints);
    }

    private Vector2Int ToWorldPoint(Vector2Int localPoint, Vector2Int currentMapOffset, int mapSize)
    {
        return new Vector2Int(
            localPoint.x + currentMapOffset.x * mapSize,
            localPoint.y + currentMapOffset.y * mapSize);
    }

    private bool WouldMakeSquare(Vector2Int point, HashSet<Vector2Int> roadWorldPoints)
    {
        Vector2Int[] origins =
        {
            point,
            point + Vector2Int.left,
            point + Vector2Int.down,
            point + Vector2Int.left + Vector2Int.down,
        };

        foreach (Vector2Int origin in origins)
        {
            if (roadWorldPoints.Contains(origin) &&
                roadWorldPoints.Contains(origin + Vector2Int.right) &&
                roadWorldPoints.Contains(origin + Vector2Int.up) &&
                roadWorldPoints.Contains(origin + Vector2Int.right + Vector2Int.up))
            {
                return true;
            }
        }

        return false;
    }
}