using UnityEngine;

namespace CombatSpace
{
    // 생성된 전투맵을 Gizmo로 표시
    public sealed class CombatMapDebugView : MonoBehaviour
    {
        [SerializeField]
        private CombatMapGenerator mapGenerator;

        [Header("Colors")]
        [SerializeField]
        private Color roadColor =new Color(0.55f, 0.3f, 0.1f);

        [SerializeField]
        private Color grassColor =new Color(0.2f, 0.65f, 0.2f);

        [SerializeField]
        private Color waterColor =new Color(0.15f, 0.45f, 0.9f);

        [SerializeField]
        private Color waypointColor =Color.yellow;

        [SerializeField]
        private Color startColor =Color.red;

        private void OnDrawGizmos()
        {
            if (mapGenerator == null ||
                mapGenerator.CurrentMap == null)
            {
                return;
            }

            CombatMapData map =
                mapGenerator.CurrentMap;

            // 이 게임 오브젝트의 위치와 회전을 기준으로 표시
            Gizmos.matrix =transform.localToWorldMatrix;

            DrawTiles(map);
            DrawWaypoints(map);
            DrawStartPosition(map);
        }

        private float TileSize
        {
            get
            {
                if (mapGenerator == null ||
                    mapGenerator.Settings == null)
                {
                    return 1f;
                }

                return mapGenerator.Settings.TileSize;
            }
        }
        private void DrawTiles(
            CombatMapData map)
        {
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position =new Vector2Int(x, y);

                    CombatTileData tile =map.GetTile(position);

                    if (tile.Type ==CombatTileType.Empty)
                    {
                        continue;
                    }

                    Gizmos.color =GetTileColor(tile.Type);

                    Vector3 center =GridToLocal(position);

                    Vector3 size =new Vector3(TileSize * 0.9f,0.05f, TileSize * 0.9f);

                    Gizmos.DrawCube(center, size);
                }
            }
        }

        private void DrawWaypoints(
            CombatMapData map)
        {
            Gizmos.color = waypointColor;

            foreach (Vector2Int waypoint in map.MajorWaypoints)
            {
                Vector3 center =GridToLocal(waypoint);

                center.y = 0.15f;

                Gizmos.DrawSphere(center, TileSize * 0.4f);
            }
        }

        private void DrawStartPosition(CombatMapData map)
        {
            Gizmos.color = startColor;

            Vector3 center =GridToLocal(map.RouteStartPosition);

            center.y = 0.2f;

            Gizmos.DrawSphere(center, TileSize * 0.55f);
        }

        private Color GetTileColor(CombatTileType type)
        {
            return type switch
            {
                CombatTileType.Road =>roadColor,

                CombatTileType.Grass =>grassColor,

                CombatTileType.Water =>waterColor,

                _ =>Color.clear
            };
        }

        private Vector3 GridToLocal(Vector2Int position)
        {
            float tileHeight =mapGenerator != null &&mapGenerator.Settings != null? mapGenerator.Settings.TileHeight: 0f;

            return new Vector3((position.x + 0.5f) * TileSize,tileHeight,(position.y + 0.5f) * TileSize);
        }
    }
}