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

        // Grass 타일에 배정된 외형과 스탯 효과 정의
        public BuffTileDefinition BuffDefinition { get; private set; }
        // 적 이동 경로 순서
        public int RouteIndex { get; private set; }

        // Road 여부
        public bool IsRoad =>
            Type == CombatTileType.Road;

        public CombatTileData(Vector2Int position)
        {
            Position = position;
            Type = CombatTileType.Empty;
            RouteIndex = -1;
            BuffDefinition = null;
        }

        // 빈 공간으로 변경
        public void SetEmpty()
        {
            Type = CombatTileType.Empty;
            RouteIndex = -1;
            BuffDefinition = null;
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
            BuffDefinition = null;
        }

        // 잔디로 변경
        public void SetGrass()
        {
            Type = CombatTileType.Grass;
            RouteIndex = -1;
            BuffDefinition = null;
        }

        // 물로 변경
        public void SetWater()
        {
            Type = CombatTileType.Water;
            RouteIndex = -1;
            BuffDefinition = null;
        }
        /// <summary>
        /// Grass 타일에 외형과 스탯 효과 정의를 배정한다.
        /// </summary>
        public void SetBuffDefinition(BuffTileDefinition definition)
        {
            if (Type != CombatTileType.Grass)
            {
                throw new InvalidOperationException("Grass 타일에만 버프 타일 정의를 지정할 수 있습니다.");
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            BuffDefinition = definition;
        }
    }
}