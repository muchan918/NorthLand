using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class StageBuilder : MonoBehaviour
{
    [SerializeField] private GameObject grassCube;
    [SerializeField] private GameObject roadCube;
    [SerializeField] private GameObject lavaCube;
    [SerializeField] private GameObject finalCenterObject;
    [SerializeField] private Vector3 finalCenterObjectOffset = Vector3.up;
    [SerializeField] private Transform battlespace;
    [SerializeField] private StageRouteSettings routeSettings = new StageRouteSettings();

    [SerializeField]
    private List<StageWaypoint> waypoints = new List<StageWaypoint>
    {
        new StageWaypoint { direction = StageWaypointDirection.Top, point = new Vector2Int(1, 0) },
        new StageWaypoint { direction = StageWaypointDirection.Top, point = new Vector2Int(2, 0) },
        new StageWaypoint { direction = StageWaypointDirection.Top, point = new Vector2Int(3, 0) },
        new StageWaypoint { direction = StageWaypointDirection.Top, point = new Vector2Int(4, 0) },
        new StageWaypoint { direction = StageWaypointDirection.Top, point = new Vector2Int(5, 0) },

        new StageWaypoint { direction = StageWaypointDirection.Left, point = new Vector2Int(0, 1) },
        new StageWaypoint { direction = StageWaypointDirection.Left, point = new Vector2Int(0, 2) },
        new StageWaypoint { direction = StageWaypointDirection.Left, point = new Vector2Int(0, 3) },
        new StageWaypoint { direction = StageWaypointDirection.Left, point = new Vector2Int(0, 4) },
        new StageWaypoint { direction = StageWaypointDirection.Left, point = new Vector2Int(0, 5) },

        new StageWaypoint { direction = StageWaypointDirection.Bottom, point = new Vector2Int(1, 6) },
        new StageWaypoint { direction = StageWaypointDirection.Bottom, point = new Vector2Int(2, 6) },
        new StageWaypoint { direction = StageWaypointDirection.Bottom, point = new Vector2Int(3, 6) },
        new StageWaypoint { direction = StageWaypointDirection.Bottom, point = new Vector2Int(4, 6) },
        new StageWaypoint { direction = StageWaypointDirection.Bottom, point = new Vector2Int(5, 6) },

        new StageWaypoint { direction = StageWaypointDirection.Right, point = new Vector2Int(6, 1) },
        new StageWaypoint { direction = StageWaypointDirection.Right, point = new Vector2Int(6, 2) },
        new StageWaypoint { direction = StageWaypointDirection.Right, point = new Vector2Int(6, 3) },
        new StageWaypoint { direction = StageWaypointDirection.Right, point = new Vector2Int(6, 4) },
        new StageWaypoint { direction = StageWaypointDirection.Right, point = new Vector2Int(6, 5) },
    };

    private const int MapSize = 7;
    private const int MaxPathGenerateTryCount = 100;

    private readonly Vector2Int[] centerPoints =
    {
        new Vector2Int(4, 3),
        new Vector2Int(3, 3),
        new Vector2Int(2, 3),
               new Vector2Int(3, 4),
                         new Vector2Int(3, 2),
    };

    private readonly List<Vector2Int> path = new List<Vector2Int>();
    private readonly List<Vector2Int> lava = new List<Vector2Int>();
    private readonly List<Vector2Int> occupiedMapOffsets = new List<Vector2Int>();
    private readonly List<GameObject> spawnedTiles = new List<GameObject>();
    private readonly List<Vector2Int> generatedMapOffsets = new List<Vector2Int>();

    private LavaGenerator lavaGenerator;
    private StageMapSpawner mapSpawner;
    private StageMapRouteGenerator routeGenerator;
    private StageTilePathBuilder tilePathBuilder;
    private StageRoadTracker roadTracker;

    private Vector2Int currentMapOffset = Vector2Int.zero;
    private Vector2Int currentStartPoint = new Vector2Int(6, 3);
    private StageWaypointDirection currentStartDirection = StageWaypointDirection.Right;
    private int currentMapCount;

    private void Awake()
    {
        PathSquareValidator squareValidator = new PathSquareValidator();
        PathGenerator pathGenerator = new PathGenerator(squareValidator);
        StageConnectionManager connectionManager = new StageConnectionManager(MapSize, waypoints);
        roadTracker = new StageRoadTracker(MapSize);

        lavaGenerator = new LavaGenerator(MapSize, 9, 12);
        mapSpawner = new StageMapSpawner(MapSize, grassCube, roadCube, lavaCube, battlespace);
        routeGenerator = new StageMapRouteGenerator();
        tilePathBuilder = new StageTilePathBuilder(
            pathGenerator,
            connectionManager,
            centerPoints,
            MapSize,
            MaxPathGenerateTryCount
        );

        PrepareRoute();
        GenerateNextStage();
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.nKey.wasPressedThisFrame)
        {
            GenerateNextStage();
        }

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            ResetStage();
        }
    }

    private void GenerateNextStage()
    {
        if (currentMapCount >= routeSettings.MaxMapCount || currentMapCount >= generatedMapOffsets.Count)
        {
            Debug.Log("설정된 맵 개수를 모두 생성했습니다.");
            return;
        }

        currentMapOffset = generatedMapOffsets[currentMapCount];

        if (occupiedMapOffsets.Contains(currentMapOffset))
        {
            Debug.LogWarning("이미 타일이 있는 위치에는 새 스테이지를 만들 수 없습니다.");
            return;
        }

        lava.Clear();

        StageTilePathBuildResult tilePathBuildResult = new StageTilePathBuildResult();

        if (!TryBuildCurrentStagePath(tilePathBuildResult))
        {
            return;
        }

        lavaGenerator.Generate(path, lava);

        mapSpawner.CreateMap(currentMapOffset, path, lava, spawnedTiles);
        SpawnFinalCenterObject(tilePathBuildResult);
        roadTracker.AddPath(currentMapOffset, path);

        occupiedMapOffsets.Add(currentMapOffset);
        currentMapCount++;
    }


    private bool TryBuildCurrentStagePath(StageTilePathBuildResult tilePathBuildResult)
    {
        bool isLastMap = currentMapCount == generatedMapOffsets.Count - 1;

        if (isLastMap)
        {
            if (tilePathBuilder.TryBuildToCenter(
                    currentMapOffset,
                    currentStartPoint,
                    roadTracker.RoadWorldPoints,
                    path,
                    tilePathBuildResult))
            {
                return true;
            }

            Debug.LogWarning($"마지막 맵의 센터 경로 생성에 실패했습니다. {currentMapOffset}");
            return false;
        }

        Vector2Int nextMapOffset = generatedMapOffsets[currentMapCount + 1];
        if (!tilePathBuilder.TryBuild(
                currentMapOffset,
                nextMapOffset,
                currentStartPoint,
                currentStartDirection,
                occupiedMapOffsets,
                roadTracker.RoadWorldPoints,
                path,
                tilePathBuildResult))
        {
            Debug.LogWarning($"확정된 맵 경로의 길 생성에 실패했습니다. {currentMapOffset} -> {nextMapOffset}");
            return false;
        }

        currentStartDirection = tilePathBuildResult.NextStartDirection;
        currentStartPoint = tilePathBuildResult.NextStartPoint;
        return true;
    }

    private void SpawnFinalCenterObject(StageTilePathBuildResult tilePathBuildResult)
    {
        bool isLastMap = currentMapCount == generatedMapOffsets.Count - 1;

        if (!isLastMap || finalCenterObject == null)
        {
            return;
        }

        Vector2Int centerPoint = tilePathBuildResult.FinalCenterPoint;
        Vector3 tileCenterPosition = new Vector3(
            centerPoint.x + currentMapOffset.x * MapSize,
            0,
            centerPoint.y + currentMapOffset.y * MapSize
        );

        GameObject spawnedObject = Object.Instantiate(finalCenterObject, battlespace);
        spawnedObject.transform.localPosition = tileCenterPosition + finalCenterObjectOffset;
        spawnedTiles.Add(spawnedObject);
    }
    private void PrepareRoute()
    {
        generatedMapOffsets.Clear();

        if (!routeGenerator.TryGenerateRoute(
                routeSettings.MaxMapCount,
                routeSettings.MinMapX,
                routeSettings.MaxMapX,
                routeSettings.MinMapY,
                routeSettings.MaxMapY,
                routeSettings.RouteGenerateTryCount,
                generatedMapOffsets))
        {
            Debug.LogWarning("조건에 맞는 전체 맵 경로를 만들지 못했습니다.");
            return;
        }

        Debug.Log($"확정된 맵 경로: {string.Join(", ", generatedMapOffsets)}");
    }

    private void ResetStage()
    {
        foreach (GameObject tile in spawnedTiles)
        {
            Destroy(tile);
        }

        spawnedTiles.Clear();
        occupiedMapOffsets.Clear();
        roadTracker.Clear();

        path.Clear();
        lava.Clear();
        generatedMapOffsets.Clear();

        currentMapOffset = Vector2Int.zero;
        currentStartPoint = new Vector2Int(6, 3);
        currentStartDirection = StageWaypointDirection.Right;
        currentMapCount = 0;

        PrepareRoute();
        GenerateNextStage();
    }
}





