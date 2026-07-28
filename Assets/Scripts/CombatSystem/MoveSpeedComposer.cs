using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    public sealed class MoveSpeedComposer
    {
        private readonly float fallbackMoveSpeed;
        private readonly float minMoveSpeed;

        private readonly Dictionary<int, float> speedDebuffs = new();

        private float baseMoveSpeed;
        private float patternSpeedFactor = 1f;
        private float speedDebuffProduct = 1f;

        public float EffectiveMoveSpeed { get; private set; }

        public float PatternSpeedFactor
        {
            get => patternSpeedFactor;
            set
            {
                patternSpeedFactor = Mathf.Max(0f, value);
                Recompute();
            }
        }

        public MoveSpeedComposer(float fallbackMoveSpeed, float minMoveSpeed)
        {
            this.fallbackMoveSpeed = Mathf.Max(0.01f, fallbackMoveSpeed);
            this.minMoveSpeed = Mathf.Max(0f, minMoveSpeed);

            baseMoveSpeed = this.fallbackMoveSpeed;
            Recompute();
        }

        public bool SetBaseMoveSpeed(float value)
        {
            bool usedFallback = value <= 0f;

            baseMoveSpeed = usedFallback? fallbackMoveSpeed: value;

            Recompute();
            return usedFallback;
        }

        public void AddSpeedDebuff(int sourceId, float factor)
        {
            speedDebuffs[sourceId] = Mathf.Max(0f, factor);
            RecomputeDebuffProduct();
        }

        public void RemoveSpeedDebuff(int sourceId)
        {
            if (speedDebuffs.Remove(sourceId))
            {
                RecomputeDebuffProduct();
            }
        }

        private void RecomputeDebuffProduct()
        {
            speedDebuffProduct = 1f;

            foreach (float factor in speedDebuffs.Values)
            {
                speedDebuffProduct *= factor;
            }

            Recompute();
        }

        private void Recompute()
        {
            EffectiveMoveSpeed = Mathf.Max(minMoveSpeed,baseMoveSpeed * patternSpeedFactor * speedDebuffProduct);
        }
    }
}