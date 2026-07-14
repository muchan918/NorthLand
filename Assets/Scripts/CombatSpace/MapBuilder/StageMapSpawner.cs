using System.Collections.Generic;
using UnityEngine;

public class StageMapSpawner
{
    private readonly int mapSize;
    private readonly float tileSize;
    private readonly GameObject grassCube;
    private readonly GameObject roadCube;
    private readonly GameObject lavaCube;
    private readonly Transform parent;

    public StageMapSpawner(
        int mapSize,
        float tileSize,
        GameObject grassCube,
        GameObject roadCube,
        GameObject lavaCube,
        Transform parent)
    {
        this.mapSize = mapSize;
        this.tileSize = tileSize;
        this.grassCube = grassCube;
        this.roadCube = roadCube;
        this.lavaCube = lavaCube;
        this.parent = parent;
    }

    public void CreateMap(
        Vector2Int mapOffset,
        List<Vector2Int> path,
        List<Vector2Int> lava,
        List<GameObject> spawnedTiles)
    {
        for (int z = 0; z < mapSize; z++)
        {
            for (int x = 0; x < mapSize; x++)
            {
                Vector2Int position = new Vector2Int(x, z);
                GameObject prefab = GetTilePrefab(position, path, lava);

                GameObject tile = Object.Instantiate(prefab, parent);

                tile.transform.localPosition = new Vector3(
                    (x + mapOffset.x * mapSize) * tileSize,
                    0,
                    (z + mapOffset.y * mapSize) * tileSize
                );

                spawnedTiles.Add(tile);
            }
        }
    }

    private GameObject GetTilePrefab(
        Vector2Int position,
        List<Vector2Int> path,
        List<Vector2Int> lava)
    {
        if (path.Contains(position))
        {
            return roadCube;
        }

        if (lava.Contains(position))
        {
            return lavaCube;
        }

        return grassCube;
    }
}
