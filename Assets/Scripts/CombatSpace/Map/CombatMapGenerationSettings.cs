using UnityEngine;

namespace CombatSpace
{
    // 전투맵 생성에 사용하는 설정값
    [CreateAssetMenu( fileName = "CombatMapGenerationSettings", menuName = "Combat Space/Map Generation Settings")]
    public sealed class CombatMapGenerationSettings :  ScriptableObject
    {
        [Header("Grid")]
        [Min(1)]
        public int Width = 70;

        [Min(1)]
        public int Height = 70;

        [Min(0)]
        public int MapMargin = 7;

        // 그리드 한 칸의 월드 크기
        [Min(0.01f)]
        public float TileSize = 5f;

        // 타일을 배치할 로컬 Y 높이
        public float TileHeight = 0f;

        [Header("Waypoints")]
        [Min(2)]
        public int MinWaypointCount = 8;

        [Min(2)]
        public int MaxWaypointCount = 12;

        [Min(1)]
        public int MinWaypointDistance = 15;

        [Min(1)]
        public int MaxWaypointPlacementAttempts = 30;

        [Header("Route")]
        [Min(0f)]
        public float RouteNoiseScale = 0.08f;

        [Min(0f)]
        public float RouteNoiseWeight = 3f;

        [Min(0f)]
        public float TurnPenalty = 0.5f;

        public Vector2Int RouteStartPosition =  new Vector2Int(0, 35);

        [Header("Grass")]
        [Min(1)]
        public int GrassRadius = 5;

        [Header("Buff Tiles")]
        [Tooltip("이번 맵에서 생성할 잔디 타일 종류와 가중치")]
        public BuffTileSpawnPool BuffTilePool;

        [Tooltip("여러 타일을 점유할 때 적용할 효과별 중첩 규칙")]
        public TileBuffRuleSettings BuffTileRules;

        [Header("Erosion")]
        [Range(0f, 1f)]
        public float ErosionProbability = 0.3f;

        [Min(0)]
        public int MinErosionIterations = 3;

        [Min(0)]
        public int MaxErosionIterations = 5;

        [Header("Water")]
        [Min(0f)]
        public float WaterNoiseScale = 0.05f;

        [Range(0f, 1f)]
        public float WaterThreshold = 0.55f;

        [Min(0)]
        public int WaterRoadClearance = 2;

        [Min(1)]
        public int MinWaterArea = 3;

        [Range(0f, 1f)]
        public float MaxWaterRatio = 0.3f;

        [Range(0, 5)]
        public int WaterSmoothingIterations = 0;

        // 설정값이 맵 생성에 사용 가능한지 검사
        public bool Validate( out string errorMessage)
        {
            if (Width < 1 ||  Height < 1)
            {
                errorMessage = "Width와 Height는 1 이상이어야 합니다.";

                return false;
            }

            if (MapMargin < 0)
            {
                errorMessage =  "MapMargin은 0보다 작을 수 없습니다.";

                return false;
            }
            if (TileSize <= 0f)
            {
                errorMessage =
                    "TileSize는 0보다 커야 합니다.";

                return false;
            }

            int usableWidth =  Width - MapMargin * 2;

            int usableHeight = Height - MapMargin * 2;

            if (usableWidth <= 0 || usableHeight <= 0)
            {
                errorMessage = "MapMargin 때문에 사용할 수 있는 영역이 없습니다.";

                return false;
            }

            if (MinWaypointCount < 2 ||   MaxWaypointCount < MinWaypointCount)
            {
                errorMessage = "웨이포인트 최소·최대 개수가 잘못됐습니다.";

                return false;
            }

            // 현재 셀 구조는 최대 4×3
            if (MaxWaypointCount > 12)
            {
                errorMessage =
                    "웨이포인트는 최대 12개까지 사용할 수 있습니다.";

                return false;
            }

            int requiredColumns = MaxWaypointCount <= 9  ? 3 : 4;

            const int requiredRows = 3;

            if (usableWidth < requiredColumns || usableHeight < requiredRows)
            {
                errorMessage =  "웨이포인트 셀을 만들기에 맵이 너무 작습니다.";

                return false;
            }

            if (MinWaypointDistance < 1)
            {
                errorMessage =  "웨이포인트 최소 거리는 1 이상이어야 합니다.";

                return false;
            }

            if (MaxWaypointPlacementAttempts < 1)
            {
                errorMessage = "웨이포인트 배치 시도 횟수는 1 이상이어야 합니다.";

                return false;
            }

            bool startInside =  RouteStartPosition.x >= 0 && RouteStartPosition.x < Width &&  RouteStartPosition.y >= 0 &&
                                 RouteStartPosition.y < Height;

            if (!startInside)
            {
                errorMessage = $"시작 좌표 {RouteStartPosition}가 " +  $"맵 {Width}×{Height} 범위를 벗어났습니다.";

                return false;
            }

            if (RouteNoiseScale < 0f ||  RouteNoiseWeight < 0f ||  TurnPenalty < 0f)
            {
                errorMessage = "Road 관련 수치는 0보다 작을 수 없습니다.";

                return false;
            }

            if (GrassRadius < 1)
            {
                errorMessage =  "GrassRadius는 1 이상이어야 합니다.";

                return false;
            }

            if (BuffTilePool == null)
            {
                errorMessage =
                    "버프 타일 생성 풀이 지정되지 않았습니다.";

                return false;
            }

            if (!BuffTilePool.Validate(
                    out string buffTilePoolError))
            {
                errorMessage =
                    "버프 타일 생성 풀 설정 오류: " +
                    buffTilePoolError;

                return false;
            }

            if (BuffTileRules == null)
            {
                errorMessage =
                    "버프 타일 중첩 규칙이 지정되지 않았습니다.";

                return false;
            }

            if (!BuffTileRules.Validate(
                    out string buffTileRuleError))
            {
                errorMessage =
                    "버프 타일 중첩 규칙 설정 오류: " +
                    buffTileRuleError;

                return false;
            }

            if (ErosionProbability < 0f ||  ErosionProbability > 1f)
            {
                errorMessage = "침식 확률은 0~1이어야 합니다.";

                return false;
            }

            if (MinErosionIterations < 0 ||  MaxErosionIterations < MinErosionIterations)
            {
                errorMessage =  "침식 최소·최대 반복 횟수가 잘못됐습니다.";

                return false;
            }

            if (WaterNoiseScale < 0f)
            {
                errorMessage =  "WaterNoiseScale은 0보다 작을 수 없습니다.";

                return false;
            }

            if (WaterThreshold < 0f ||  WaterThreshold > 1f ||  MaxWaterRatio < 0f || MaxWaterRatio > 1f)
            {
                errorMessage =   "Water 확률 관련 수치는 0~1이어야 합니다.";

                return false;
            }

            if (WaterRoadClearance < 0 ||  MinWaterArea < 1 ||   WaterSmoothingIterations < 0)
            {
                errorMessage = "Water 관련 설정값이 잘못됐습니다.";

                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}