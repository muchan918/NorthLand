using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    // 주요 웨이포인트를 균등하게 생성
    public sealed class WaypointGenerator
    {
        public bool Generate( CombatMapData map,CombatMapGenerationSettings settings, System.Random random)
        {
            map.MajorWaypoints.Clear();

            int waypointCount = random.Next(  settings.MinWaypointCount, settings.MaxWaypointCount + 1);

            List<RectInt> cells = CreateCells( map.Width, map.Height, settings.MapMargin,waypointCount);

            ShuffleCells( cells, random);

            float minimumDistance = settings.MinWaypointDistance;

            for (int i = 0; i < waypointCount; i++)
            {
                bool success =  TryCreateWaypoint( cells[i], map.MajorWaypoints,minimumDistance,
                        settings.MaxWaypointPlacementAttempts, random,out Vector2Int waypoint);

                if (!success)
                {
                    map.MajorWaypoints.Clear();

                    return false;
                }

                map.MajorWaypoints.Add(waypoint);
            }

            return true;
        }

        // 맵 내부를 웨이포인트 생성 셀로 분할
        private List<RectInt> CreateCells(int mapWidth, int mapHeight,int margin, int waypointCount)
        {
            int columns = waypointCount <= 9 ? 3 : 4;

            const int rows = 3;

            int usableWidth = mapWidth - margin * 2;

            int usableHeight = mapHeight - margin * 2;

            int cellWidth = usableWidth / columns;

            int cellHeight = usableHeight / rows;

            List<RectInt> cells = new List<RectInt>( columns * rows);

            for (int y = 0;   y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int cellX =  margin + x * cellWidth;

                    int cellY = margin + y * cellHeight;

                    int width =  x == columns - 1 ? usableWidth -  cellWidth * x : cellWidth;

                    int height = y == rows - 1 ? usableHeight -  cellHeight * y  : cellHeight;

                    cells.Add(new RectInt(  cellX, cellY, width, height));
                }
            }

            return cells;
        }

        // 사용할 셀 순서를 무작위로 섞음
        private void ShuffleCells( List<RectInt> cells,System.Random random)
        {
            for (int i = cells.Count - 1;i > 0;  i--)
            {
                int swapIndex =  random.Next(i + 1);

                (cells[i], cells[swapIndex]) = (cells[swapIndex], cells[i]);
            }
        }

        // 한 셀 안에서 웨이포인트 생성 시도
        private bool TryCreateWaypoint( RectInt cell,  List<Vector2Int> existingWaypoints, float initialMinimumDistance,
                                       int maxAttempts, System.Random random, out Vector2Int waypoint)
        {
            float minimumDistance = initialMinimumDistance;

            while (minimumDistance >= 1f)
            {
                for (int attempt = 0; attempt < maxAttempts;  attempt++)
                {
                    Vector2Int candidate = new Vector2Int( random.Next(  cell.xMin,  cell.xMax),

                            random.Next( cell.yMin, cell.yMax));

                    if (IsFarEnough(  candidate,  existingWaypoints, minimumDistance))
                    {
                        waypoint = candidate;

                        return true;
                    }
                }

                // 반복 실패 시 최소 거리를 한 칸 완화
                minimumDistance -= 1f;
            }

            waypoint = default;

            return false;
        }

        // 기존 웨이포인트와 최소 거리 확인
        private bool IsFarEnough( Vector2Int candidate, List<Vector2Int> existingWaypoints, float minimumDistance)
        {
            float minimumDistanceSquared =  minimumDistance *  minimumDistance;

            foreach (Vector2Int existing  in existingWaypoints)
            {
                Vector2Int difference =  candidate - existing;

                if (difference.sqrMagnitude <  minimumDistanceSquared)
                {
                    return false;
                }
            }

            return true;
        }
    }
}