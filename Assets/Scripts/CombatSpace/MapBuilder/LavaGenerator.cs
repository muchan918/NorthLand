using System.Collections.Generic;
using UnityEngine;

public class LavaGenerator
{
    private readonly int mapSize;
    private readonly int minLavaCount;
    private readonly int maxLavaCount;

    public LavaGenerator(int mapSize, int minLavaCount, int maxLavaCount)
    {
        this.mapSize = mapSize;
        this.minLavaCount = minLavaCount;
        this.maxLavaCount = maxLavaCount;
    }

    public void Generate(List<Vector2Int> path, List<Vector2Int> lava)
    {
        lava.Clear();

        List<Vector2Int> candidates = GetCandidates(path);
        MapRandom.Shuffle(candidates);

        int lavaCount = Random.Range(minLavaCount, maxLavaCount + 1);
        lavaCount = Mathf.Min(lavaCount, candidates.Count);

        for (int i = 0; i < lavaCount; i++)
        {
            lava.Add(candidates[i]);
        }
    }

    private List<Vector2Int> GetCandidates(List<Vector2Int> path)
    {
        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int z = 0; z < mapSize; z++)
        {
            for (int x = 0; x < mapSize; x++)
            {
                Vector2Int position = new Vector2Int(x, z);

                if (!path.Contains(position) && !IsNextToPath(position, path))
                {
                    candidates.Add(position);
                }
            }
        }

        return candidates;
    }

    private bool IsNextToPath(Vector2Int position, List<Vector2Int> path)
    {
        Vector2Int[] directions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right,
        };

        foreach (Vector2Int direction in directions)
        {
            if (path.Contains(position + direction))
            {
                return true;
            }
        }

        return false;
    }
}