using System;
using UnityEngine;

namespace CombatSpace
{
    // 전투맵 타일 한 칸의 데이터
    public sealed class CombatTileData
    {
        // 그리드 좌표
        public Vector2Int Position { get; }

        // 현재 타일 종류
        public CombatTileType Type { get; private set; }

        // 적 이동 경로 순서
        public int RouteIndex { get; private set; }

        // Road 여부
        public bool IsRoad =>
            Type == CombatTileType.Road;

        public CombatTileData(
            Vector2Int position)
        {
            Position = position;
            Type = CombatTileType.Empty;
            RouteIndex = -1;
        }

        // 빈 공간으로 변경
        public void SetEmpty()
        {
            Type = CombatTileType.Empty;
            RouteIndex = -1;
        }

        // 길로 변경
        public void SetRoad(int routeIndex)
        {
            if (routeIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(routeIndex));
            }

            Type = CombatTileType.Road;
            RouteIndex = routeIndex;
        }

        // 잔디로 변경
        public void SetGrass()
        {
            Type = CombatTileType.Grass;
            RouteIndex = -1;
        }

        // 물로 변경
        public void SetWater()
        {
            Type = CombatTileType.Water;
            RouteIndex = -1;
        }
    }
}