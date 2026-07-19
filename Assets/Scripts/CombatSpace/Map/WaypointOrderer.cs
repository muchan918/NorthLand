using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    // 웨이포인트의 연결 순서를 결정
    public sealed class WaypointOrderer
    {
        public bool Order(CombatMapData map)
        {
            if (map.MajorWaypoints.Count < 2)
            {
                return false;
            }

            List<Vector2Int> ordered = CreateNearestNeighborOrder(map.MajorWaypoints, map.RouteStartPosition);

            ImproveWithTwoOpt(ordered);

            map.MajorWaypoints.Clear();

            map.MajorWaypoints.AddRange(ordered);

            return true;
        }

        // 시작점과 가장 가까운 웨이포인트 검색
        private int FindStartIndex(List<Vector2Int> waypoints,  Vector2Int routeStartPosition)
        {
            int bestIndex = 0;

            int bestDistance = (waypoints[0] -routeStartPosition).sqrMagnitude;

            for (int i = 1;i < waypoints.Count; i++)
            {
                int distance = (waypoints[i] -routeStartPosition) .sqrMagnitude;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        // 현재 위치에서 가장 가까운 노드를 반복 선택
        private List<Vector2Int> CreateNearestNeighborOrder(List<Vector2Int> waypoints, Vector2Int routeStartPosition)
        {
            List<Vector2Int> remaining =  new List<Vector2Int>(waypoints);

            List<Vector2Int> ordered = new List<Vector2Int>(waypoints.Count);

            int startIndex = FindStartIndex(remaining, routeStartPosition);

            Vector2Int current =  remaining[startIndex];

            ordered.Add(current);

            remaining.RemoveAt(startIndex);

            while (remaining.Count > 0)
            {
                int nearestIndex = FindNearestIndex( current,remaining);

                current = remaining[nearestIndex];

                ordered.Add(current);

                remaining.RemoveAt( nearestIndex);
            }

            return ordered;
        }

        // 후보 중 현재 좌표와 가장 가까운 좌표 검색
        private int FindNearestIndex( Vector2Int current, List<Vector2Int> candidates)
        {
            int nearestIndex = 0;

            int nearestDistance = (candidates[0] - current).sqrMagnitude;

            for (int i = 1; i < candidates.Count; i++)
            {
                int distance = (candidates[i] - current) .sqrMagnitude;

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        // 2-opt로 불필요하게 긴 연결과 교차 개선
        private void ImproveWithTwoOpt( List<Vector2Int> route)
        {
            if (route.Count < 4)
            {
                return;
            }

            bool improved = true;

            while (improved)
            {
                improved = false;

                for (int i = 1;  i < route.Count - 2;i++)
                {
                    for (int k = i + 1; k < route.Count - 1; k++)
                    {
                        Vector2Int a =route[i - 1];

                        Vector2Int b =route[i];

                        Vector2Int c =route[k];

                        Vector2Int d =route[k + 1];

                        int currentDistance =(a - b).sqrMagnitude + (c - d).sqrMagnitude;

                        int changedDistance = (a - c).sqrMagnitude + (b - d).sqrMagnitude;

                        if (changedDistance <currentDistance)
                        {
                            ReverseRange(route, i, k);

                            improved = true;
                        }
                    }
                }
            }
        }

        // 지정된 구간의 순서를 뒤집음
        private void ReverseRange( List<Vector2Int> route, int startIndex, int endIndex)
        {
            while (startIndex < endIndex)
            {
                (route[startIndex], route[endIndex]) =  (route[endIndex], route[startIndex]);

                startIndex++;
                endIndex--;
            }
        }
    }
}