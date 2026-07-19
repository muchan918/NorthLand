using UnityEngine;

namespace CombatSpace
{
    // 전투맵 생성 과정 전체를 관리
    public sealed class CombatMapGenerator : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField]
        private CombatMapGenerationSettings settings;

        [Header("Seed")]
        [SerializeField]
        private int seed = 12345;

        private readonly WaypointGenerator waypointGenerator =new WaypointGenerator();

        private readonly WaypointOrderer waypointOrderer =new WaypointOrderer();

        private readonly RouteGenerator routeGenerator =new RouteGenerator();

        private readonly RouteValidator routeValidator =new RouteValidator();

        private readonly GrassGenerator grassGenerator =new GrassGenerator();

        private readonly GrassEroder grassEroder =new GrassEroder();

        private readonly WaterGenerator waterGenerator =new WaterGenerator();

        // 가장 최근에 생성된 맵 데이터
        public CombatMapData CurrentMap { get; private set; }

        [ContextMenu("Generate Map")]
        public void Generate()
        {
            if (settings == null)
            {
                Debug.LogError("맵 생성 설정이 지정되지 않았습니다.",this);

                return;
            }

            if (!settings.Validate(out string settingsError))
            {
                Debug.LogError($"맵 생성 설정 오류: {settingsError}",this);

                return;
            }

            System.Random random = new System.Random(seed);

            CurrentMap = new CombatMapData(settings.Width,settings.Height,seed,settings.RouteStartPosition);

            if (!waypointGenerator.Generate(CurrentMap,settings,random))
            {
                Debug.LogError("웨이포인트 생성에 실패했습니다.",this);

                return;
            }

            if (!waypointOrderer.Order(CurrentMap))
            {
                Debug.LogError("웨이포인트 순서 결정에 실패했습니다.",this);

                return;
            }

            if (!routeGenerator.Generate(CurrentMap,settings,random))
            {
                Debug.LogError("Road 경로 생성에 실패했습니다.",this);

                return;
            }

            if (!routeValidator.Validate(CurrentMap,out string routeError))
            {
                Debug.LogError($"Road 검증 실패: {routeError}",this);

                return;
            }

            if (!grassGenerator.Generate(CurrentMap,settings))
            {
                Debug.LogError("Grass 생성에 실패했습니다.",this);

                return;
            }

            if (!grassEroder.Erode(CurrentMap,settings,random))
            {
                Debug.LogError("Grass 침식에 실패했습니다.",this);

                return;
            }

            if (!waterGenerator.Generate(CurrentMap,settings,random))
            {
                Debug.LogError("Water 생성에 실패했습니다.",this);

                return;
            }

            // Water 생성 후에도 Road 상태가 정상인지 최종 검사
            if (!routeValidator.Validate(CurrentMap,out string finalRouteError))
            {
                Debug.LogError($"최종 Road 검증 실패: {finalRouteError}",this);

                return;
            }

            PrintGenerationResult();
        }

        private void PrintGenerationResult()
        {
            int roadCount = CountTiles(CombatTileType.Road);

            int grassCount = CountTiles(CombatTileType.Grass);

            int waterCount = CountTiles(CombatTileType.Water);

            Debug.Log(
                "전투맵 생성 완료\n" +
                $"Seed: {seed}\n" +
                $"Waypoints: " +
                $"{CurrentMap.MajorWaypoints.Count}개\n" +
                $"Road: {roadCount}칸\n" +
                $"Grass: {grassCount}칸\n" +
                $"Water: {waterCount}칸",
                this);
        }

        private int CountTiles(CombatTileType tileType)
        {
            int count = 0;

            for (int x = 0;x < CurrentMap.Width;x++)
            {
                for (int y = 0;y < CurrentMap.Height;y++)
                {
                    Vector2Int position = new Vector2Int(x, y);

                    if (CurrentMap.GetTile(position).Type == tileType)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }
}