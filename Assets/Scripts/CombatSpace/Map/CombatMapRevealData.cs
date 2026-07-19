using System;
using UnityEngine;

namespace CombatSpace
{
    // 전투맵 타일의 공개 상태를 저장
    public sealed class CombatMapRevealData
    {
        private readonly bool[,] revealedTiles;

        public int Width { get; }

        public int Height { get; }

        public int RevealedTileCount{get;private set;}

        public CombatMapRevealData(int width,int height)
        {
            if (width < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Width = width;
            Height = height;

            revealedTiles =
                new bool[width, height];
        }

        public bool IsRevealed(Vector2Int position)
        {
            if (!IsInside(position))
            {
                return false;
            }

            return revealedTiles[position.x,position.y];
        }

        public bool Reveal(Vector2Int position)
        {
            if (!IsInside(position) ||revealedTiles[position.x,position.y])
            {
                return false;
            }

            revealedTiles[position.x,position.y] = true;

            RevealedTileCount++;

            return true;
        }

        public void Clear()
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    revealedTiles[x, y] = false;
                }
            }

            RevealedTileCount = 0;
        }

        private bool IsInside(
            Vector2Int position)
        {
            return
                position.x >= 0 &&
                position.x < Width &&
                position.y >= 0 &&
                position.y < Height;
        }
    }
}