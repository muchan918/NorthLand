using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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

public class StageBuilder : MonoBehaviour
{
    [SerializeField] private GameObject grassCube;
    [SerializeField] private GameObject roadCube;
    [SerializeField] private GameObject lavaCube;
    [SerializeField] private Transform battlespace;

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
        new Vector2Int(3, 3),
        new Vector2Int(4, 4),
        new Vector2Int(2, 2),
    };

    private readonly List<Vector2Int> path = new List<Vector2Int>();
    private readonly List<Vector2Int> lava = new List<Vector2Int>();
    private readonly List<Vector2Int> occupiedMapOffsets = new List<Vector2Int>();
    private readonly List<Vector3> totalWorldPath = new List<Vector3>();
    private readonly List<GameObject> spawnedTiles = new List<GameObject>();

    private PathGenerator pathGenerator;
    private LavaGenerator lavaGenerator;
    private StageConnectionManager connectionManager;
    private StageMapSpawner mapSpawner;

    private Vector2Int currentMapOffset = Vector2Int.zero;
    private Vector2Int currentStartPoint = new Vector2Int(6, 3);
    private StageWaypointDirection currentStartDirection = StageWaypointDirection.Right;

    private void Awake()
    {
        pathGenerator = new PathGenerator();
        lavaGenerator = new LavaGenerator(MapSize, 9, 12);
        connectionManager = new StageConnectionManager(MapSize, waypoints);
        mapSpawner = new StageMapSpawner(MapSize, grassCube, roadCube, lavaCube, battlespace);

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
        if (occupiedMapOffsets.Contains(currentMapOffset))
        {
            Debug.LogWarning("이미 타일이 있는 위치에는 새 스테이지를 만들 수 없습니다.");
            return;
        }

        bool success = false;
        StageWaypoint selectedEndWaypoint = null;

        for (int i = 0; i < MaxPathGenerateTryCount; i++)
        {
            path.Clear();
            lava.Clear();

            if (!connectionManager.TryGetRandomEndPoint(
                    currentStartDirection,
                    currentMapOffset,
                    occupiedMapOffsets,
                    out StageWaypoint endWaypoint))
            {
                break;
            }

            Vector2Int centerPoint = centerPoints[Random.Range(0, centerPoints.Length)];

            success = pathGenerator.TryGeneratePath(
                currentStartPoint,
                centerPoint,
                endWaypoint.point,
                path
            );

            if (success)
            {
                selectedEndWaypoint = endWaypoint;
                break;
            }
        }

        if (!success)
        {
            Debug.LogWarning("길 생성에 실패했습니다.");
            return;
        }

        lavaGenerator.Generate(path, lava);

        mapSpawner.CreateMap(currentMapOffset, path, lava, spawnedTiles);
        AddCurrentPathToTotalWorldPath(currentMapOffset);

        occupiedMapOffsets.Add(currentMapOffset);

        currentMapOffset = connectionManager.GetNextMapOffset(
            currentMapOffset,
            selectedEndWaypoint.direction
        );

        currentStartDirection = connectionManager.GetOppositeDirection(
            selectedEndWaypoint.direction
        );

        currentStartPoint = connectionManager.GetOppositeStartPoint(
            selectedEndWaypoint
        );
    }

    private void AddCurrentPathToTotalWorldPath(Vector2Int mapOffset)
    {
        foreach (Vector2Int point in path)
        {
            Vector3 worldPosition = new Vector3(
                point.x + mapOffset.x * MapSize,
                0,
                point.y + mapOffset.y * MapSize
            );

            totalWorldPath.Add(worldPosition);
        }
    }

    public List<Vector3> GetTotalWorldPath()
    {
        return totalWorldPath;
    }

    private void ResetStage()
    {
        foreach (GameObject tile in spawnedTiles)
        {
            Destroy(tile);
        }

        spawnedTiles.Clear();
        occupiedMapOffsets.Clear();

        path.Clear();
        lava.Clear();
        totalWorldPath.Clear();

        currentMapOffset = Vector2Int.zero;
        currentStartPoint = new Vector2Int(6, 3);
        currentStartDirection = StageWaypointDirection.Right;

        GenerateNextStage();
    }
}