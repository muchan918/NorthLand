using System;
using UnityEngine;

namespace CombatSpace
{
    // 타일 하나의 논리 정보를 실제 GameObject와 연결
    public sealed class CombatMapTileView : MonoBehaviour
    {
        [SerializeField]
        [HideInInspector]
        private Vector2Int gridPosition;

        [SerializeField]
        [HideInInspector]
        private CombatTileType tileType;

        [SerializeField]
        [HideInInspector]
        private int routeIndex = -1;

        public Vector2Int GridPosition => gridPosition;

        public CombatTileType TileType => tileType;

        public int RouteIndex => routeIndex;

        public BuffTileDefinition BuffDefinition { get; private set; }


        public void Initialize(CombatTileData tileData)
        {
            if (tileData == null)
            {
                throw new ArgumentNullException(nameof(tileData));
            }

            gridPosition = tileData.Position;
            tileType = tileData.Type;
            routeIndex = tileData.RouteIndex;
            BuffDefinition = tileData.BuffDefinition;

            gameObject.name =$"Tile_{tileType}_{gridPosition.x}_{gridPosition.y}";
        }
    }
}