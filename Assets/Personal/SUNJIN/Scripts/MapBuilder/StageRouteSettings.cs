using UnityEngine;

[System.Serializable]
public class StageRouteSettings
{
    [SerializeField] private int maxMapCount = 30;
    [SerializeField] private int routeGenerateTryCount = 500;
    [SerializeField] private int minMapX = -6;
    [SerializeField] private int maxMapX = -1;
    [SerializeField] private int minMapY = -3;
    [SerializeField] private int maxMapY = 3;

    public int MaxMapCount => maxMapCount;
    public int RouteGenerateTryCount => routeGenerateTryCount;
    public int MinMapX => minMapX;
    public int MaxMapX => maxMapX;
    public int MinMapY => minMapY;
    public int MaxMapY => maxMapY;
}
