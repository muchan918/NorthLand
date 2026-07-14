using System.Collections.Generic;
using UnityEngine;

public class StageMonsterRouteTracker
{
    private readonly int mapSize;
    private readonly float tileSize;
    private readonly Transform parent;
    private readonly List<Vector3> route = new List<Vector3>();

    public List<Vector3> Route => route;

    public StageMonsterRouteTracker(int mapSize, float tileSize, Transform parent)
    {
        this.mapSize = mapSize;
        this.tileSize = tileSize;
        this.parent = parent;
    }

    public void AddPath(Vector2Int mapOffset, List<Vector2Int> localPath)
    {
        for (int i = 0; i < localPath.Count; i++)
        {
            Vector3 worldPosition = ToWorldPosition(mapOffset, localPath[i]);

            if (route.Count > 0 && route[route.Count - 1] == worldPosition)
            {
                continue;
            }

            route.Add(worldPosition);
        }
    }

    public Vector3 GetWorldPosition(Vector2Int mapOffset, Vector2Int localPoint)
    {
        return ToWorldPosition(mapOffset, localPoint);
    }

    public void Clear()
    {
        route.Clear();
    }

    private Vector3 ToWorldPosition(Vector2Int mapOffset, Vector2Int localPoint)
    {
        Vector3 localPosition = new Vector3(
            (localPoint.x + mapOffset.x * mapSize) * tileSize,
            0,
            (localPoint.y + mapOffset.y * mapSize) * tileSize
        );

        return parent != null
            ? parent.TransformPoint(localPosition)
            : localPosition;
    }
}
