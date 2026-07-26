using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    /// <summary>
    /// 이번 맵에서 생성할 수 있는 잔디 타일 종류와
    /// 각 타일의 활성화 여부 및 가중치를 관리한다.
    /// </summary>
    [CreateAssetMenu(fileName = "BuffTileSpawnPool",menuName = "Combat Space/Buff Tile Spawn Pool")]
    public sealed class BuffTileSpawnPool :
        ScriptableObject
    {
        [SerializeField]
        [Tooltip("이번 맵 생성에 사용할 잔디 타일 목록")]
        private List<BuffTileSpawnEntry> entries =new List<BuffTileSpawnEntry>();

        public IReadOnlyList<BuffTileSpawnEntry> Entries =>entries;

        /// <summary>
        /// 현재 생성 가능한 항목의 전체 가중치를 반환한다.
        /// </summary>
        public float GetTotalWeight()
        {
            float totalWeight = 0f;

            foreach (BuffTileSpawnEntry entry in entries)
            {
                if (entry == null ||!entry.IsAvailable)
                {
                    continue;
                }

                totalWeight += entry.Weight;
            }

            return totalWeight;
        }

        /// <summary>
        /// 생성 풀이 정상적으로 설정됐는지 검사한다.
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            if (entries == null ||entries.Count == 0)
            {
                errorMessage = "버프 타일 생성 항목이 없습니다.";

                return false;
            }

            var registeredIds =new HashSet<string>();

            int availableCount = 0;

            foreach (BuffTileSpawnEntry entry in entries)
            {
                if (entry == null)
                {
                    errorMessage = "비어 있는 버프 타일 생성 항목이 있습니다.";

                    return false;
                }

                if (!entry.Enabled)
                {
                    continue;
                }

                if (entry.Definition == null)
                {
                    errorMessage ="활성화된 생성 항목에 타일 Definition이 없습니다.";

                    return false;
                }

                if (string.IsNullOrWhiteSpace(entry.Definition.Id))
                {
                    errorMessage =
                        "활성화된 버프 타일의 ID가 없습니다.";

                    return false;
                }

                if (entry.Definition.Prefab == null)
                {
                    errorMessage =$"버프 타일 '{entry.Definition.Id}'에 프리팹이 없습니다.";

                    return false;
                }

                foreach (TileStatModifier modifier in entry.Definition.Modifiers)
                {
                    if (modifier.Stat ==TileBuffStat.AttackSpeed &&modifier.ModifierMode ==TileModifierMode.Flat)
                    {
                        errorMessage =$"버프 타일 '{entry.Definition.Id}'의 AttackSpeed는 Percentage 모드만 지원합니다.";

                        return false;
                    }
                }

                if (entry.Weight <= 0f)
                {
                    errorMessage =$"버프 타일 '{entry.Definition.Id}'의 가중치는 0보다 커야 합니다.";

                    return false;
                }

                if (!registeredIds.Add(entry.Definition.Id))
                {
                    errorMessage =$"버프 타일 ID '{entry.Definition.Id}'가 중복됐습니다.";

                    return false;
                }

                availableCount++;
            }

            if (availableCount == 0)
            {
                errorMessage ="현재 생성 가능한 버프 타일이 없습니다.";

                return false;
            }

            if (GetTotalWeight() <= 0f)
            {
                errorMessage ="활성화된 버프 타일의 전체 가중치가 0입니다.";

                return false;
            }

            errorMessage = null;

            return true;
        }
    }
}