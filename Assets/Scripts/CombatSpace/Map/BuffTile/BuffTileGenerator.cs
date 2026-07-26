using System.Collections.Generic;

namespace CombatSpace
{
    /// <summary>
    /// 최종 Grass 타일에 SpawnPool의 가중치에 따라
    /// 일반 잔디 또는 버프 타일 Definition을 배정한다.
    /// </summary>
    public sealed class BuffTileGenerator
    {
        /// <summary>
        /// 맵의 모든 Grass 타일에 Definition을 배정한다.
        /// </summary>
        public bool Generate(CombatMapData map,CombatMapGenerationSettings settings,System.Random random)
        {
            if (map == null ||settings == null ||random == null)
            {
                return false;
            }

            BuffTileSpawnPool spawnPool =settings.BuffTilePool;

            if (spawnPool == null ||!spawnPool.Validate(out _))
            {
                return false;
            }

            IReadOnlyList<BuffTileSpawnEntry> entries =spawnPool.Entries;

            float totalWeight =spawnPool.GetTotalWeight();

            if (totalWeight <= 0f)
            {
                return false;
            }

            for (int x = 0;x < map.Width;x++)
            {
                for (int y = 0;y < map.Height;y++)
                {
                    var position =new UnityEngine.Vector2Int(x,y);

                    CombatTileData tile =map.GetTile(position);

                    if (tile.Type !=CombatTileType.Grass)
                    {
                        continue;
                    }

                    BuffTileDefinition definition =SelectDefinition(entries,totalWeight,random);

                    if (definition == null)
                    {
                        return false;
                    }

                    tile.SetBuffDefinition(definition);
                }
            }

            return true;
        }

        /// <summary>
        /// 활성화된 항목 중 하나를 가중치 기반으로 선택한다.
        /// </summary>
        private BuffTileDefinition SelectDefinition(IReadOnlyList<BuffTileSpawnEntry> entries,float totalWeight,System.Random random)
        {
            double roll =random.NextDouble() *totalWeight;

            BuffTileDefinition fallback = null;

            foreach (BuffTileSpawnEntry entry in entries)
            {
                if (entry == null ||!entry.IsAvailable)
                {
                    continue;
                }

                fallback = entry.Definition;

                roll -= entry.Weight;

                if (roll <= 0d)
                {
                    return entry.Definition;
                }
            }

            // 부동소수점 오차로 마지막 경계를 지나친 경우
            // 마지막 유효 항목을 반환한다.
            return fallback;
        }
    }
}