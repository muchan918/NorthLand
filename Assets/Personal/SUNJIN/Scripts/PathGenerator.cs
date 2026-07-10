using System.Collections.Generic;
using UnityEngine;

public class PathGenerator
{
    public bool TryGeneratePath(
        Vector2Int startPoint,
        Vector2Int centerPoint,
        Vector2Int endPoint,
        List<Vector2Int> path)
    {
        path.Clear();

        bool startToCenter = AddPath(startPoint, centerPoint, path);
        bool centerToEnd = AddPath(centerPoint, endPoint, path);

        return startToCenter && centerToEnd;
    }

    private bool AddPath(Vector2Int from, Vector2Int to, List<Vector2Int> path)
    {
        Vector2Int current = from;

        AddPointIfNotExists(current, path);

        while (current != to)
        {
            List<Vector2Int> candidates = GetCandidates(current, to, path);

            if (candidates.Count == 0)
            {
                return false;
            }

            if (!TryGetNextPoint(candidates, path, out Vector2Int nextPoint))
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
        List<Vector2Int> path,
        out Vector2Int nextPoint)
    {
        MapRandom.Shuffle(candidates);

        foreach (Vector2Int candidate in candidates)
        {
            path.Add(candidate);
            bool makesSquare = WouldMakeSquare(path);
            path.Remove(candidate);

            if (!makesSquare)
            {
                nextPoint = candidate;
                return true;
            }
        }

        nextPoint = Vector2Int.zero;
        return false;
    }

    private void AddPointIfNotExists(Vector2Int point, List<Vector2Int> path)
    {
        if (!path.Contains(point))
        {
            path.Add(point);
        }
    }

    private bool WouldMakeSquare(List<Vector2Int> path)
    {
        foreach (Vector2Int point in path)
        {
            Vector2Int right = point + Vector2Int.right;
            Vector2Int up = point + Vector2Int.up;
            Vector2Int diagonal = point + Vector2Int.right + Vector2Int.up;

            if (path.Contains(right) &&
                path.Contains(up) &&
                path.Contains(diagonal))
            {
                return true;
            }
        }

        return false;
    }
}