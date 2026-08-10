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

        [Header("Buff Icon")]
        [SerializeField]
        [Tooltip("버프 아이콘을 담는 자식 오브젝트")]
        private GameObject buffIconRoot;

        [SerializeField]
        [Tooltip("버프 아이콘을 표시할 SpriteRenderer")]
        private SpriteRenderer buffIconRenderer;


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

            ConfigureBuffIcon();


            gameObject.name =$"Tile_{tileType}_{gridPosition.x}_{gridPosition.y}";
        }
        private void ConfigureBuffIcon()
        {
            if (buffIconRoot == null || buffIconRenderer == null)
            {
                return;
            }

            buffIconRenderer.sprite = BuffDefinition != null ? BuffDefinition.Icon : null;

            // 평상시에는 아이콘을 숨긴다.
            buffIconRoot.SetActive(false);
        }

        public void SetBuffIconVisible(bool visible)
        {
            if (buffIconRoot == null || buffIconRenderer == null)
            {
                return;
            }

            bool hasIcon = BuffDefinition != null &&BuffDefinition.Icon != null;

            buffIconRoot.SetActive(visible && hasIcon);
        }
    }
}