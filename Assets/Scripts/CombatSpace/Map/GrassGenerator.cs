using UnityEngine;

namespace CombatSpace
{
    // Road 주변에 Grass 영토를 생성
    public sealed class GrassGenerator
    {
        public bool Generate( CombatMapData map,CombatMapGenerationSettings settings)
        {
            if (map == null ||settings == null ||map.EnemyRoute.Count == 0)
            {
                return false;
            }

            foreach (Vector2Int roadPosition in map.EnemyRoute)
            {
                FillGrassAroundRoad(map,roadPosition,settings.GrassRadius);
            }

            return true;
        }

        // Road 한 칸을 중심으로 일정 범위에 Grass 생성
        private void FillGrassAroundRoad(CombatMapData map,Vector2Int roadPosition,int radius)
        {
            for (int offsetX = -radius;offsetX <= radius;offsetX++)
            {
                for (int offsetY = -radius;offsetY <= radius;offsetY++)
                {
                    // 원형 범위 밖의 타일은 제외
                    int squaredDistance =offsetX * offsetX +offsetY * offsetY;

                    if (squaredDistance >radius * radius)
                    {
                        continue;
                    }

                    Vector2Int position =roadPosition +new Vector2Int(offsetX,offsetY);

                    if (!map.IsInside(position))
                    {
                        continue;
                    }

                    CombatTileData tile =map.GetTile(position);

                    // Road 등 이미 사용 중인 타일은 변경하지 않음
                    if (tile.Type !=CombatTileType.Empty)
                    {
                        continue;
                    }

                    tile.SetGrass();
                }
            }
        }
    }
}