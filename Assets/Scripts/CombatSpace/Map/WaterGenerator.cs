using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace CombatSpace
{
    // Grass 영역에 노이즈 기반 Water 지형을 생성
    public sealed class WaterGenerator
    {
        private static readonly Vector2Int[] FourDirections =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        public bool Generate(CombatMapData map,CombatMapGenerationSettings settings,Random random)
        {
            if (map == null ||settings == null ||random == null)
            {
                return false;
            }

            float noiseOffsetX =(float)random.NextDouble() * 10000f;

            float noiseOffsetY =(float)random.NextDouble() * 10000f;

            GenerateWaterCandidates(map,settings,noiseOffsetX,noiseOffsetY);

            RemoveSmallWaterAreas(map,settings.MinWaterArea);

            SmoothWater(map,settings);

            // 스무딩으로 생긴 작은 물 영역 정리
            RemoveSmallWaterAreas(map,settings.MinWaterArea);

            LimitWaterRatio(map,settings.MaxWaterRatio,random);

            // 비율 제한 이후 다시 정리
            RemoveSmallWaterAreas(map,settings.MinWaterArea);

            return true;
        }
        // 주변 Water 개수를 이용해 외곽 형태 정리
        private void SmoothWater(CombatMapData map,CombatMapGenerationSettings settings)
        {
            for (int iteration = 0;iteration < settings.WaterSmoothingIterations;iteration++)
            {
                List<Vector2Int> waterToGrass = new List<Vector2Int>();

                List<Vector2Int> grassToWater = new List<Vector2Int>();

                for (int x = 0; x < map.Width; x++)
                {
                    for (int y = 0; y < map.Height; y++)
                    {
                        Vector2Int position = new Vector2Int(x, y);

                        CombatTileData tile = map.GetTile(position);

                        int waterNeighborCount =CountWaterNeighbors(map,position);

                        // 주변 Water가 너무 적은 돌출 부분 제거
                        if (tile.Type ==CombatTileType.Water &&waterNeighborCount <= 2)
                        {
                            waterToGrass.Add(position);
                            continue;
                        }

                        // Water에 둘러싸인 Grass 공간 채우기
                        if (tile.Type ==CombatTileType.Grass &&waterNeighborCount >= 5 && 
                            !IsNearRoad(map,position,settings.WaterRoadClearance))
                        {
                            grassToWater.Add(position);
                        }
                    }
                }

                // 검사 도중에는 타일을 변경하지 않고
                // 모든 검사가 끝난 후 한꺼번에 적용
                foreach (Vector2Int position in waterToGrass)
                {
                    map.GetTile(position).SetGrass();
                }

                foreach (Vector2Int position in grassToWater)
                {
                    map.GetTile(position).SetWater();
                }
            }
        }

        // 주변 8칸의 Water 개수 계산
        private int CountWaterNeighbors(CombatMapData map,Vector2Int position)
        {
            int count = 0;

            for (int offsetX = -1;offsetX <= 1;offsetX++)
            {
                for (int offsetY = -1;offsetY <= 1;offsetY++)
                {
                    if (offsetX == 0 &&offsetY == 0)
                    {
                        continue;
                    }

                    Vector2Int neighborPosition =position +new Vector2Int(offsetX,offsetY);

                    if (!map.IsInside(neighborPosition))
                    {
                        continue;
                    }

                    CombatTileData neighbor = map.GetTile(neighborPosition);

                    if (neighbor.Type == CombatTileType.Water)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
        // 퍼린 노이즈를 이용해 Water 후보 생성
        private void GenerateWaterCandidates(CombatMapData map,CombatMapGenerationSettings settings,float noiseOffsetX,float noiseOffsetY)
        {
            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position = new Vector2Int(x, y);

                    CombatTileData tile = map.GetTile(position);

                    if (tile.Type != CombatTileType.Grass)
                    {
                        continue;
                    }

                    if (IsNearRoad(map,position,settings.WaterRoadClearance))
                    {
                        continue;
                    }

                    float noiseX =(x + noiseOffsetX) *settings.WaterNoiseScale;

                    float noiseY =(y + noiseOffsetY) *settings.WaterNoiseScale;

                    float noiseValue =Mathf.PerlinNoise(noiseX,noiseY);

                    if (noiseValue <settings.WaterThreshold)
                    {
                        continue;
                    }

                    tile.SetWater();
                }
            }
        }

        // 지정한 반경 안에 Road가 있는지 검사
        private bool IsNearRoad(CombatMapData map,Vector2Int position, int clearance)
        {
            int squaredClearance = clearance * clearance;

            for (int offsetX = -clearance;offsetX <= clearance;offsetX++)
            {
                for (int offsetY = -clearance;offsetY <= clearance;offsetY++)
                {
                    int squaredDistance =offsetX * offsetX +offsetY * offsetY;

                    if (squaredDistance >squaredClearance)
                    {
                        continue;
                    }

                    Vector2Int checkPosition =position +new Vector2Int(offsetX,offsetY);

                    if (!map.IsInside(checkPosition))
                    {
                        continue;
                    }

                    CombatTileData checkTile = map.GetTile(checkPosition);

                    if (checkTile.Type ==CombatTileType.Road)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // 최소 크기보다 작은 Water 영역 제거
        private void RemoveSmallWaterAreas(CombatMapData map,int minimumArea)
        {
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position = new Vector2Int(x, y);

                    if (visited.Contains(position))
                    {
                        continue;
                    }

                    CombatTileData tile = map.GetTile(position);

                    if (tile.Type != CombatTileType.Water)
                    {
                        continue;
                    }

                    List<Vector2Int> waterArea =CollectWaterArea(map,position,visited);

                    if (waterArea.Count >=minimumArea)
                    {
                        continue;
                    }

                    RestoreAreaToGrass(map,waterArea);
                }
            }
        }

        // 하나로 연결된 Water 영역을 BFS로 수집
        private List<Vector2Int> CollectWaterArea(CombatMapData map,Vector2Int startPosition,HashSet<Vector2Int> visited)
        {
            List<Vector2Int> area = new List<Vector2Int>();

            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            queue.Enqueue(startPosition);
            visited.Add(startPosition);

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();

                area.Add(current);

                foreach (Vector2Int direction in FourDirections)
                {
                    Vector2Int next = current + direction;

                    if (!map.IsInside(next) || visited.Contains(next))
                    {
                        continue;
                    }

                    CombatTileData tile = map.GetTile(next);

                    if (tile.Type != CombatTileType.Water)
                    {
                        continue;
                    }

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            return area;
        }

        // 작은 Water 영역을 Grass로 복구
        private void RestoreAreaToGrass(CombatMapData map,List<Vector2Int> area)
        {
            foreach (Vector2Int position in area)
            {
                map.GetTile(position).SetGrass();
            }
        }

        // Water가 최대 비율을 넘지 않도록 제한
        private void LimitWaterRatio(CombatMapData map,float maximumRatio,Random random)
        {
            int playableTileCount = CountPlayableTiles(map);

            int maximumWaterCount =Mathf.FloorToInt(playableTileCount *maximumRatio);

            int currentWaterCount =CountWaterTiles(map);

            while (currentWaterCount >maximumWaterCount)
            {
                List<Vector2Int> boundaries =FindWaterBoundaryTiles(map);

                if (boundaries.Count == 0)
                {
                    break;
                }

                Shuffle(boundaries, random);

                int removeCount =Mathf.Min(currentWaterCount -maximumWaterCount,boundaries.Count);

                for (int i = 0;i < removeCount;i++)
                {
                    map.GetTile(boundaries[i]).SetGrass();

                    currentWaterCount--;
                }
            }
        }

        // Road, Grass, Water를 전체 플레이 영역으로 계산
        private int CountPlayableTiles(CombatMapData map)
        {
            int count = 0;

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position = new Vector2Int(x, y);

                    CombatTileType type = map.GetTile(position).Type;

                    if (type != CombatTileType.Empty)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private int CountWaterTiles(CombatMapData map)
        {
            int count = 0;

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position =new Vector2Int(x, y);

                    if (map.GetTile(position).Type ==CombatTileType.Water)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        // Grass 또는 맵 바깥과 맞닿은 Water 검색
        private List<Vector2Int> FindWaterBoundaryTiles(CombatMapData map)
        {
            List<Vector2Int> boundaries = new List<Vector2Int>();

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position = new Vector2Int(x, y);

                    if (map.GetTile(position).Type !=CombatTileType.Water)
                    {
                        continue;
                    }

                    if (IsWaterBoundary(map,position))
                    {
                        boundaries.Add(position);
                    }
                }
            }

            return boundaries;
        }

        private bool IsWaterBoundary(CombatMapData map,Vector2Int position)
        {
            foreach (Vector2Int direction
                     in FourDirections)
            {
                Vector2Int neighborPosition = position + direction;

                if (!map.IsInside(neighborPosition))
                {
                    return true;
                }

                CombatTileType neighborType = map.GetTile(neighborPosition).Type;

                if (neighborType != CombatTileType.Water)
                {
                    return true;
                }
            }

            return false;
        }

        private void Shuffle(List<Vector2Int> positions,Random random)
        {
            for (int i = positions.Count - 1;i > 0;i--)
            {
                int swapIndex = random.Next(i + 1);

                (positions[i], positions[swapIndex]) = (positions[swapIndex], positions[i]);
            }
        }
    }
}