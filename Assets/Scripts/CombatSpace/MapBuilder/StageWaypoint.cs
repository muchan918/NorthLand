using UnityEngine;

public enum StageWaypointDirection
{
    Top,
    Bottom,
    Left,
    Right
}

[System.Serializable]
public class StageWaypoint
{
    public StageWaypointDirection direction;
    public Vector2Int point;
}