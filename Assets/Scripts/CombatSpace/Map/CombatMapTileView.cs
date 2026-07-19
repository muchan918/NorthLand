using System;
using UnityEngine;

namespace CombatSpace
{
    // 타일 하나의 논리 정보를 실제 GameObject와 연결
    public sealed class CombatMapTileView : MonoBehaviour
    {
        public Vector2Int GridPosition {get;private set;}

        public CombatTileType TileType {get;private set;}

        public int RouteIndex {get;private set;}

        public void Initialize(CombatTileData tileData)
        {
            if (tileData == null)
            {
                throw new ArgumentNullException(
                nameof(tileData));
            }

            GridPosition =tileData.Position;

            TileType =tileData.Type;

            RouteIndex =tileData.RouteIndex;

            gameObject.name =$"Tile_{TileType}_{GridPosition.x}_{GridPosition.y}";
        }
    }
}