using System;
using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    // 전투맵 전체의 논리 데이터
    public sealed class CombatMapData
    {
        // 맵 크기
        public int Width { get; }

        public int Height { get; }

        // 생성에 사용된 시드
        public int Seed { get; }

        // 적 경로 시작 좌표
        public Vector2Int RouteStartPosition { get; }

        // 전체 타일 데이터
        public CombatTileData[,] Tiles { get; }

        // 주요 웨이포인트
        public List<Vector2Int> MajorWaypoints { get; }

        // 적이 이동할 Road 전체 경로
        public List<Vector2Int> EnemyRoute { get; }

        public CombatMapData( int width, int height,int seed, Vector2Int routeStartPosition)
        {
            if (width < 1)
            {
                throw new ArgumentOutOfRangeException( nameof(width));
            }

            if (height < 1)
            {
                throw new ArgumentOutOfRangeException( nameof(height));
            }

            Width = width;
            Height = height;
            Seed = seed;
            RouteStartPosition = routeStartPosition;

            Tiles = new CombatTileData[width, height];

            MajorWaypoints = new List<Vector2Int>();

            EnemyRoute = new List<Vector2Int>();

            InitializeTiles();
        }

        // 모든 타일을 생성
        private void InitializeTiles()
        {
            for (int x = 0;  x < Width; x++)
            {
                for (int y = 0;  y < Height;  y++)
                {
                    Vector2Int position =  new Vector2Int(x, y);

                    Tiles[x, y] =  new CombatTileData(position);
                }
            }
        }

        // 좌표가 맵 내부인지 확인
        public bool IsInside( Vector2Int position)
        {
            return
                position.x >= 0 &&
                position.x < Width &&
                position.y >= 0 &&
                position.y < Height;
        }

        // 해당 좌표의 타일 반환
        public CombatTileData GetTile(    Vector2Int position)
        {
            if (!IsInside(position))
            {
                return null;
            }

            return Tiles[ position.x,position.y];
        }
    }
}