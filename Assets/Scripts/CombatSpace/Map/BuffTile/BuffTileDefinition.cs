using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    /// <summary>
    /// 버프 타일 한 종류의 외형과 스탯 효과를 정의한다.
    ///
    /// 예:
    /// - 일반 잔디
    /// - 사거리 +1
    /// - 사거리 +2
    /// - 공격력 +10%
    /// </summary>
    [CreateAssetMenu(fileName = "BuffTileDefinition",menuName = "Combat Space/Buff Tile Definition")]
    public sealed class BuffTileDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        [Tooltip("저장 및 데이터 구분에 사용할 고유 ID")]
        private string id;

        [Header("Visual")]
        [SerializeField]
        [Tooltip("이 타일이 선택됐을 때 생성할 프리팹")]
        private GameObject prefab;

        [Header("Effects")]
        [SerializeField]
        [Tooltip("이 타일이 건물에 부여할 스탯 효과 목록")]
        private List<TileStatModifier> modifiers = new List<TileStatModifier>();

        public string Id => id;

        public GameObject Prefab => prefab;

        public IReadOnlyList<TileStatModifier> Modifiers =>modifiers;
    }
}