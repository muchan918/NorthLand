using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;
namespace CombatSpace
{
    // Grass 경계를 침식하여 자연스러운 영토 형태를 생성
    public sealed class GrassEroder
    {
        private static readonly Vector2Int[] FourDirections =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        private bool IsNearBase(CombatMapData map,CombatMapGenerationSettings settings,Vector2Int position)
        {
            int distance =Mathf.Abs(position.x - map.BasePosition.x) +Mathf.Abs(position.y - map.BasePosition.y);

            return distance <= settings.BaseProtectionRange;
        }

        public bool Erode(CombatMapData map,CombatMapGenerationSettings settings,Random random)
        {
            if (map == null ||settings == null ||random == null)
            {
                return false;
            }

            int iterationCount =random.Next(settings.MinErosionIterations,settings.MaxErosionIterations + 1);

            for (int iteration = 0;iteration < iterationCount;iteration++)
            {
                List<Vector2Int> candidates = FindBoundaryGrassTiles(map, settings);

                Shuffle(candidates, random);

                foreach (Vector2Int position in candidates)
                {
                    if (random.NextDouble() > settings.ErosionProbability)
                    {
                        continue;
                    }

                    // 주변 영토가 너무 적으면 제거하지 않음
                    if (CountTerritoryNeighbors(map,position) < 2)
                    {
                        continue;
                    }

                    CombatTileData tile =map.GetTile(position);

                    tile.SetEmpty();

                    // 연결이 끊어지면 Grass로 복구
                    if (!IsTerritoryConnected(map))
                    {
                        tile.SetGrass();
                    }
                }
            }

            return true;
        }

        // Empty와 맞닿은 Grass 경계 타일 검색
        private List<Vector2Int> FindBoundaryGrassTiles(CombatMapData map,CombatMapGenerationSettings settings)
        {
            List<Vector2Int> boundaries = new List<Vector2Int>();

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position = new Vector2Int(x, y);

                    if (IsNearBase(map,settings,position))
                    {
                        continue;
                    }

                    CombatTileData tile = map.GetTile(position);

                    if (tile.Type != CombatTileType.Grass)
                    {
                        continue;
                    }

                    if (IsBoundaryGrass(map, position))
                    {
                        boundaries.Add(position);
                    }
                }
            }

            return boundaries;
        }

        // 인접한 4칸 중 Empty 또는 맵 바깥이 있는지 검사
        private bool IsBoundaryGrass(CombatMapData map,Vector2Int position)
        {
            foreach (Vector2Int direction in FourDirections)
            {
                Vector2Int neighborPosition = position + direction;

                if (!map.IsInside(neighborPosition))
                {
                    return true;
                }

                CombatTileData neighbor =map.GetTile(neighborPosition);

                if (neighbor.Type ==CombatTileType.Empty)
                {
                    return true;
                }
            }

            return false;
        }

        // 주변 8칸의 Road 또는 Grass 개수 계산
        private int CountTerritoryNeighbors(CombatMapData map,Vector2Int position)
        {
            int count = 0;

            for (int offsetX = -1;offsetX <= 1;offsetX++)
            {
                for (int offsetY = -1;offsetY <= 1;offsetY++)
                {
                    if (offsetX == 0 &&
                        offsetY == 0)
                    {
                        continue;
                    }

                    Vector2Int neighborPosition =position +new Vector2Int(offsetX,offsetY);

                    if (!map.IsInside(neighborPosition))
                    {
                        continue;
                    }

                    CombatTileData neighbor =map.GetTile(neighborPosition);

                    if (IsTerritory(neighbor))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        // BFS로 전체 영토의 연결 상태 검사
        private bool IsTerritoryConnected(CombatMapData map)
        {
            Vector2Int? startPosition =FindFirstTerritory(map);

            if (!startPosition.HasValue)
            {
                return false;
            }

            int totalTerritoryCount =CountAllTerritory(map);

            Queue<Vector2Int> queue =new Queue<Vector2Int>();

            HashSet<Vector2Int> visited =new HashSet<Vector2Int>();

            queue.Enqueue(startPosition.Value);
            visited.Add(startPosition.Value);

            while (queue.Count > 0)
            {
                Vector2Int current =queue.Dequeue();

                foreach (Vector2Int direction in FourDirections)
                {
                    Vector2Int next =current + direction;

                    if (!map.IsInside(next) ||visited.Contains(next))
                    {
                        continue;
                    }

                    CombatTileData tile =map.GetTile(next);

                    if (!IsTerritory(tile))
                    {
                        continue;
                    }

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            return visited.Count == totalTerritoryCount;
        }

        private Vector2Int? FindFirstTerritory(CombatMapData map)
        {
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position =new Vector2Int(x, y);

                    if (IsTerritory(map.GetTile(position)))
                    {
                        return position;
                    }
                }
            }

            return null;
        }

        private int CountAllTerritory(CombatMapData map)
        {
            int count = 0;

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position =new Vector2Int(x, y);

                    if (IsTerritory(map.GetTile(position)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private bool IsTerritory(CombatTileData tile)
        {
            return
                tile.Type == CombatTileType.Road ||
                tile.Type == CombatTileType.Grass;
        }

        // 침식 순서가 한쪽으로 치우치지 않도록 섞음
        private void Shuffle(List<Vector2Int> positions,Random random)
        {
            for (int i = positions.Count - 1;
                 i > 0;
                 i--)
            {
                int swapIndex =random.Next(i + 1);

                (positions[i], positions[swapIndex]) = (positions[swapIndex], positions[i]);
            }
        }
    }
}
