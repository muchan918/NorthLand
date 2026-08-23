using UnityEngine;

// 조건이 필요로 하는 씬 참조를 모아 넘겨준다.
// 조건마다 FindFirstObjectByType을 부르지 않게 하려는 것이 목적이다.
// 새 시스템의 조건이 생기면 여기에 프로퍼티를 하나 추가한다.
public class TutorialContext
{
    private ManagementController _management;
    private CombatSpace.CombatMapTileSpawner _tileSpawner;
    private TowerSelectPanelView _towerPanel;
    private CameraController2 _camera;
    private MonsterSpawn _monsterSpawn;

    // ManagementController에는 static Instance가 없다(DayNightManager와 다르다).
    // 씬에 하나뿐이므로 처음 요청될 때 찾아서 캐시한다.
    public ManagementController Management
    {
        get
        {
            if (_management == null)
            {
                _management = Object.FindFirstObjectByType<ManagementController>();
            }

            return _management;
        }
    }

    // 스포너도 싱글턴이 아니다 — TowerPlacer.Awake도 같은 방식으로 찾는다.
    // 전투 타일을 그리드 좌표로 지목할 때 필요하다.
    public CombatSpace.CombatMapTileSpawner TileSpawner
    {
        get
        {
            if (_tileSpawner == null)
            {
                _tileSpawner = Object.FindFirstObjectByType<CombatSpace.CombatMapTileSpawner>();
            }

            return _tileSpawner;
        }
    }

    // 타워 패널에도 static Instance가 없다. 타워 선택을 감시하는 조건이 필요로 한다.
    public TowerSelectPanelView TowerPanel
    {
        get
        {
            if (_towerPanel == null)
            {
                _towerPanel = Object.FindFirstObjectByType<TowerSelectPanelView>();
            }

            return _towerPanel;
        }
    }

    // CameraController2에도 static Instance가 없다. 카메라 조작을 감시하는 조건이 필요로 한다.
    public CameraController2 Camera
    {
        get
        {
            if (_camera == null)
            {
                _camera = Object.FindFirstObjectByType<CameraController2>();
            }

            return _camera;
        }
    }

    // MonsterSpawn에도 static Instance가 없다. 스폰·웨이브를 감시하는 조건이 필요로 한다.
    public MonsterSpawn MonsterSpawn
    {
        get
        {
            if (_monsterSpawn == null)
            {
                _monsterSpawn = Object.FindFirstObjectByType<MonsterSpawn>();
            }

            return _monsterSpawn;
        }
    }

    public DayNightManager DayNight => DayNightManager.Instance;
}
