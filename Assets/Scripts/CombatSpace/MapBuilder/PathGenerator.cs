using System.Collections.Generic;
using UnityEngine;

public class PathGenerator
{
    private readonly PathSquareValidator squareValidator;

    public PathGenerator(PathSquareValidator squareValidator)
    {
        this.squareValidator = squareValidator;
    }

    public bool TryGeneratePath(
        Vector2Int startPoint,
        Vector2Int centerPoint,
        Vector2Int endPoint,
        Vector2Int currentMapOffset,
        int mapSize,
        IReadOnlyCollection<Vector2Int> generatedRoadWorldPoints,
        List<Vector2Int> path)
    {
        path.Clear();

        bool startToCenter = AddPath(
            startPoint,
            centerPoint,
            currentMapOffset,
            mapSize,
            generatedRoadWorldPoints,
            path);

        bool centerToEnd = AddPath(
            centerPoint,
            endPoint,
            currentMapOffset,
            mapSize,
            generatedRoadWorldPoints,
            path);

        return startToCenter && centerToEnd;
    }

    private bool AddPath(
        Vector2Int from,
        Vector2Int to,
        Vector2Int currentMapOffset,
        int mapSize,
        IReadOnlyCollection<Vector2Int> generatedRoadWorldPoints,
        List<Vector2Int> path)
    {
        Vector2Int current = from;

        AddPointIfNotExists(current, path);
        if (squareValidator.WouldMakeSquareWithCurrentPath(
                current,
                currentMapOffset,
                mapSize,
                generatedRoadWorldPoints,
                path))
        {
            return false;
        }

        while (current != to)
        {
            List<Vector2Int> candidates = GetCandidates(current, to, path);

            if (candidates.Count == 0)
            {
                return false;
            }

            if (!TryGetNextPoint(
                    candidates,
                    currentMapOffset,
                    mapSize,
                    generatedRoadWorldPoints,
                    path,
                    out Vector2Int nextPoint))
            {
                return false;
            }

            current = nextPoint;
            AddPointIfNotExists(current, path);
        }

        return true;
    }

    private List<Vector2Int> GetCandidates(Vector2Int current, Vector2Int to, List<Vector2Int> path)
    {
        List<Vector2Int> candidates = new List<Vector2Int>();

        if (current.x != to.x)
        {
            int xDirection = current.x < to.x ? 1 : -1;
            Vector2Int nextX = new Vector2Int(current.x + xDirection, current.y);
            AddCandidate(nextX, path, candidates);
        }

        if (current.y != to.y)
        {
            int yDirection = current.y < to.y ? 1 : -1;
            Vector2Int nextY = new Vector2Int(current.x, current.y + yDirection);
            AddCandidate(nextY, path, candidates);
        }

        return candidates;
    }

    private void AddCandidate(Vector2Int candidate, List<Vector2Int> path, List<Vector2Int> candidates)
    {
        if (!path.Contains(candidate))
        {
            candidates.Add(candidate);
        }
    }

    private bool TryGetNextPoint(
        List<Vector2Int> candidates,
        Vector2Int currentMapOffset,
        int mapSize,
        IReadOnlyCollection<Vector2Int> generatedRoadWorldPoints,
        List<Vector2Int> path,
        out Vector2Int nextPoint)
    {
        MapRandom.Shuffle(candidates);

        foreach (Vector2Int candidate in candidates)
        {
            path.Add(candidate);
            bool makesSquare = squareValidator.WouldMakeSquareWithCurrentPath(
                candidate,
                currentMapOffset,
                mapSize,
                generatedRoadWorldPoints,
                path);
            path.Remove(candidate);

            if (!makesSquare)
            {
                nextPoint = candidate;
                return true;
            }
        }

        nextPoint = candidates[0];
        return true;
    }

    private void AddPointIfNotExists(Vector2Int point, List<Vector2Int> path)
    {
        if (!path.Contains(point))
        {
            path.Add(point);
        }
    }
}