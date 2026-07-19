using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    // 생성된 Road 경로 검증
    public sealed class RouteValidator
    {
        private static readonly Vector2Int[] FourDirections =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        public bool Validate( CombatMapData map,out string errorMessage)
        {
            if (map.EnemyRoute.Count == 0)
            {
                errorMessage = "EnemyRoute가 비어 있습니다.";

                return false;
            }

            if (map.EnemyRoute[0] != map.RouteStartPosition)
            {
                errorMessage = "Road가 고정 시작점에서 시작하지 않습니다.";

                return false;
            }

            HashSet<Vector2Int> visited =new HashSet<Vector2Int>();

            for (int i = 0; i < map.EnemyRoute.Count;i++)
            {
                Vector2Int position = map.EnemyRoute[i];

                // Road 좌표가 맵 내부인지 검사
                if (!map.IsInside(position))
                {
                    errorMessage = $"{i}번 Road가 맵 밖입니다:{position}";

                    return false;
                }

                // 동일한 좌표를 두 번 사용했는지 검사
                if (!visited.Add(position))
                {
                    errorMessage = $"중복 Road 좌표가 있습니다:{position}";

                    return false;
                }

                CombatTileData tile = map.GetTile(position);

                // EnemyRoute 좌표가 실제 Road인지 검사
                if (!tile.IsRoad)
                {
                    errorMessage = $"{position}이 Road가 아닙니다.";

                    return false;
                }

                // 타일의 이동 순서가 목록 순서와 같은지 검사
                if (tile.RouteIndex != i)
                {
                    errorMessage = $"{position}의 RouteIndex가 일치하지 않습니다:{tile.RouteIndex}/{i}";

                    return false;
                }

                // 이전 Road와 상하좌우 한 칸으로 연결됐는지 검사
                if (i > 0 && !AreAdjacent( map.EnemyRoute[i - 1], position))
                {
                    errorMessage = $"{i - 1}번과 {i}번 Road가이어져 있지 않습니다.";

                    return false;
                }

                // 순서상 앞뒤가 아닌 Road와 붙었는지 검사
                if (HasUnexpectedRoadNeighbor( map,i, out Vector2Int unexpectedNeighbor))
                {
                    errorMessage = $"{i}번 Road {position}이 순서상 이웃이 아닌 Road {unexpectedNeighbor}와 붙어 있습니다.";

                    return false;
                }
            }

            // 모든 웨이포인트를 Road가 통과하는지 검사
            foreach (Vector2Int waypoint in map.MajorWaypoints)
            {
                if (!visited.Contains(waypoint))
                {
                    errorMessage = $"Road가 통과하지 않은 웨이포인트가 있습니다:{waypoint}";

                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        // 순서상 앞뒤가 아닌 Road와 붙었는지 검사
        private bool HasUnexpectedRoadNeighbor( CombatMapData map, int routeIndex, out Vector2Int unexpectedNeighbor)
        {
            Vector2Int position = map.EnemyRoute[routeIndex];

            Vector2Int? previous = routeIndex > 0 ? map.EnemyRoute[routeIndex - 1] : null;

            Vector2Int? next = routeIndex < map.EnemyRoute.Count - 1 ? map.EnemyRoute[routeIndex + 1]: null;

            foreach (Vector2Int direction in FourDirections)
            {
                Vector2Int neighborPosition = position + direction;

                if (!map.IsInside(neighborPosition))
                {
                    continue;
                }

                CombatTileData neighborTile =map.GetTile(neighborPosition);

                if (!neighborTile.IsRoad)
                {
                    continue;
                }

                // 순서상 바로 이전 Road는 허용
                if (previous.HasValue &&neighborPosition ==previous.Value)
                {
                    continue;
                }

                // 순서상 바로 다음 Road는 허용
                if (next.HasValue &&neighborPosition ==next.Value)
                {
                    continue;
                }

                unexpectedNeighbor = neighborPosition;

                return true;
            }

            unexpectedNeighbor = default;
            return false;
        }

        // 두 좌표가 상하좌우 한 칸 차이인지 검사
        private bool AreAdjacent(Vector2Int first,Vector2Int second)
        {
            int distance =Mathf.Abs(first.x - second.x) +Mathf.Abs(first.y - second.y);

            return distance == 1;
        }
    }
}