using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    // 시작점과 웨이포인트를 폭 1칸 Road로 연결
    public sealed class RouteGenerator
    {
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        // A* 탐색 상태
        private readonly struct SearchState : System.IEquatable<SearchState>
        {
            public Vector2Int Position { get; }

            public Vector2Int Direction { get; }

            public SearchState(Vector2Int position, Vector2Int direction)
            {
                Position = position;
                Direction = direction;
            }

            public bool Equals(SearchState other)
            {
                return
                    Position == other.Position &&
                    Direction == other.Direction;
            }

            public override bool Equals(object obj)
            {
                return
                    obj is SearchState other &&
                    Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return
                        Position.GetHashCode() * 397 ^Direction.GetHashCode();
                }
            }
        }

        public bool Generate( CombatMapData map, CombatMapGenerationSettings settings, System.Random random)
        {
            if (map.MajorWaypoints.Count == 0)
            {
                return false;
            }

            map.EnemyRoute.Clear();

            List<Vector2Int> targets = CreateRouteTargets(map);

            HashSet<Vector2Int> occupiedRoute = new HashSet<Vector2Int>();

            for (int i = 0; i < targets.Count - 1; i++)
            {
                Vector2Int start = targets[i];

                Vector2Int goal =targets[i + 1];

                List<Vector2Int> segment =
                    FindSegmentPath( map,settings, random, start, goal, occupiedRoute);

                if (segment == null || segment.Count == 0)
                {
                    map.EnemyRoute.Clear();

                    return false;
                }

                AppendSegment(  map.EnemyRoute,occupiedRoute, segment);
            }

            ApplyRoadTiles(map);

            return true;
        }

        // 시작점과 웨이포인트를 연결 대상에 추가
        private List<Vector2Int> CreateRouteTargets( CombatMapData map)
        {
            List<Vector2Int> targets = new List<Vector2Int>(  map.MajorWaypoints.Count + 1);

            targets.Add(map.RouteStartPosition);

            targets.AddRange(map.MajorWaypoints);

            return targets;
        }

        // 생성된 구간을 전체 경로에 추가
        private void AppendSegment( List<Vector2Int> enemyRoute, HashSet<Vector2Int> occupiedRoute, List<Vector2Int> segment)
        {
            // 첫 구간이 아니면 시작 좌표 중복 제외
            int startIndex =  enemyRoute.Count == 0  ? 0 : 1;

            for (int i = startIndex; i < segment.Count; i++)
            {
                Vector2Int position =segment[i];

                enemyRoute.Add(position);
                occupiedRoute.Add(position);
            }
        }

        // 전체 경로를 Road 타일로 변경
        private void ApplyRoadTiles(CombatMapData map)
        {
            for (int i = 0; i < map.EnemyRoute.Count; i++)
            {
                Vector2Int position =map.EnemyRoute[i];

                CombatTileData tile =  map.GetTile(position);

                tile.SetRoad(i);
            }
        }

        // 노이즈 가중 A*로 한 구간 생성
        private List<Vector2Int> FindSegmentPath( CombatMapData map, CombatMapGenerationSettings settings, System.Random random, Vector2Int start,
                                                  Vector2Int goal, HashSet<Vector2Int> occupiedRoute)
        {
            float noiseOffsetX =  random.Next( -100000,100000);

            float noiseOffsetY =random.Next( -100000, 100000);

            SearchState startState =new SearchState(start, Vector2Int.zero);

            List<SearchState> openSet = new List<SearchState>
                {
                    startState
                };

            HashSet<SearchState> closedSet = new HashSet<SearchState>();

            Dictionary<SearchState, SearchState> cameFrom = new Dictionary< SearchState,SearchState>();

            Dictionary<SearchState, float> gScore =new Dictionary<SearchState, float>
                {
                    [startState] = 0f
                };

            Dictionary<SearchState, float> fScore =new Dictionary<SearchState, float>
                {
                    [startState] =GetHeuristic(start,goal)
                };

            while (openSet.Count > 0)
            {
                int bestIndex = FindLowestScoreIndex( openSet,fScore);

                SearchState current =openSet[bestIndex];

                openSet.RemoveAt(bestIndex);

                if (current.Position == goal)
                {
                    return ReconstructPath( current, cameFrom);
                }

                closedSet.Add(current);

                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int nextPosition = current.Position + direction;

                    if (!map.IsInside(nextPosition))
                    {
                        continue;
                    }

                    // 기존 Road와 겹치는 것 방지
                    if (occupiedRoute.Contains( nextPosition) && nextPosition != goal)
                    {
                        continue;
                    }

                    /*
                     * 새 구간의 첫 칸은 이전 구간의
                     * 마지막 Road와 붙는 것을 허용
                     */
                    bool leavingStart = current.Position == start;

                    // 기존 Road 옆으로 붙는 것 방지
                    if (IsAdjacentToOccupiedRoute(nextPosition,occupiedRoute, leavingStart ? start: (Vector2Int?)null))
                    {
                        continue;
                    }

                    SearchState nextState =new SearchState(nextPosition, direction);

                    if (closedSet.Contains(nextState))
                    {
                        continue;
                    }

                    float movementCost = 1f;

                    // 방향이 바뀌면 추가 비용
                    if (current.Direction != Vector2Int.zero &&current.Direction != direction)
                    {
                        movementCost += settings.TurnPenalty;
                    }

                    float noise =  Mathf.PerlinNoise((nextPosition.x +noiseOffsetX) *settings.RouteNoiseScale,
                                                    (nextPosition.y + noiseOffsetY) * settings.RouteNoiseScale);

                    // 높은 노이즈 영역을 선호
                    float noiseCost =(1f - noise) * settings.RouteNoiseWeight;

                    float tentativeGScore =gScore[current] +movementCost + noiseCost;

                    if (gScore.TryGetValue(  nextState, out float knownGScore) && tentativeGScore >= knownGScore)
                    {
                        continue;
                    }

                    cameFrom[nextState] = current;

                    gScore[nextState] =  tentativeGScore;

                    fScore[nextState] =tentativeGScore + GetHeuristic( nextPosition,goal);

                    if (!openSet.Contains( nextState))
                    {
                        openSet.Add(nextState);
                    }
                }
            }

            return null;
        }

        // 후보 좌표가 기존 Road 옆인지 검사
        private bool IsAdjacentToOccupiedRoute( Vector2Int candidate, HashSet<Vector2Int> occupiedRoute, Vector2Int? allowedNeighbor)
        {
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int neighbor = candidate + direction;

                if (!occupiedRoute.Contains(neighbor))
                {
                    continue;
                }

                // 구간 시작 연결은 허용
                if (allowedNeighbor.HasValue && neighbor == allowedNeighbor.Value)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        // 목표까지의 맨해튼 거리
        private float GetHeuristic(  Vector2Int current,  Vector2Int goal)
        {
            return
                Mathf.Abs(
                    current.x - goal.x) +
                Mathf.Abs(
                    current.y - goal.y);
        }

        // 가장 낮은 예상 비용을 가진 상태 검색
        private int FindLowestScoreIndex( List<SearchState> openSet, Dictionary<SearchState, float> fScore)
        {
            int bestIndex = 0;

            float bestScore = fScore[openSet[0]];

            for (int i = 1; i < openSet.Count;i++)
            {
                float score = fScore[openSet[i]];

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        // A* 결과를 좌표 목록으로 복원
        private List<Vector2Int> ReconstructPath( SearchState goalState,Dictionary<SearchState, SearchState>cameFrom)
        {
            List<Vector2Int> path = new List<Vector2Int>
                {
                    goalState.Position
                };

            SearchState current =  goalState;

            while (cameFrom.TryGetValue( current,  out SearchState previous))
            {
                current = previous;

                path.Add(
                    current.Position);
            }

            path.Reverse();

            return path;
        }
    }
}