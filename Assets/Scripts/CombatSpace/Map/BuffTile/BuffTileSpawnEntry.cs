using System;
using UnityEngine;

namespace CombatSpace
{
    /// <summary>
    /// 버프 타일 생성 풀에 등록되는 항목.
    /// 타일 정의, 활성화 여부, 생성 가중치를 보관한다.
    /// </summary>
    [Serializable]
    public sealed class BuffTileSpawnEntry
    {
        [SerializeField]
        [Tooltip("생성할 타일의 외형과 효과 정의")]
        private BuffTileDefinition definition;

        [SerializeField]
        [Tooltip("비활성화하면 가중치와 관계없이 생성되지 않는다.")]
        private bool enabled = true;

        [SerializeField]
        [Min(0f)]
        [Tooltip("활성화된 다른 타일과 비교할 상대적인 생성 비중")]
        private float weight = 1f;

        public BuffTileDefinition Definition =>definition;

        public bool Enabled =>enabled;

        public float Weight =>weight;

        /// <summary>
        /// 현재 항목을 맵 생성 후보로 사용할 수 있는지 반환한다.
        /// </summary>
        public bool IsAvailable =>enabled &&definition != null &&weight > 0f;
    }
}