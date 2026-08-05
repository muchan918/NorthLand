using CombatSpace;
using UnityEngine;

namespace NorthLand.Core
{
    public sealed partial class RunSaveManager
    {
        [Tooltip("저장된 웨이브에 맞춰 전투 맵 공개 범위를 복원하는 컨트롤러")]
        [SerializeField]
        private CombatMapRevealController combatMapRevealController;

        [SerializeField]
        private CombatMapTileSpawner combatMapTileSpawner;

        /// <summary>
        /// 저장된 웨이브 진행도에 맞춰 전투 맵의 공개 범위를 복원한다.
        /// 타워 복원보다 먼저 호출해야 저장된 타워의 셀이 활성화된다.
        /// </summary>
        private bool TryRestoreCombatMapReveal(ProgressSaveData data)
        {
            if (combatMapRevealController == null)
            {
                Debug.LogError("[Load] CombatMapRevealController가 연결되지 않았습니다.",this);

                return false;
            }

            if (data == null)
            {
                Debug.LogError("[Load] 맵 공개에 필요한 진행 상태 데이터가 없습니다.",this);

                return false;
            }

            if (data.WaveCount < 0)
            {
                Debug.LogError($"[Load] 맵 공개에 사용할 WaveCount가 유효하지 않습니다: {data.WaveCount}",this);

                return false;
            }

            if (combatMapRevealController.RevealData == null)
            {
                Debug.LogError("[Load] 전투 맵 공개 데이터가 아직 초기화되지 않았습니다.",this);

                return false;
            }

            if (combatMapTileSpawner == null)
            {
                Debug.LogError("[Load] CombatMapTileSpawner가 연결되지 않았습니다.",this);

                return false;
            }

            combatMapTileSpawner.SkipNextRevealAnimation();
            combatMapRevealController.RevealForRound(data.WaveCount);

            // 타일 Transform 변경을 즉시 물리 엔진에 반영한다.
            // 바로 다음 타워 복원에서 OverlapSphere로 타일을 찾기 위해 필요하다.
            Physics.SyncTransforms();

            return true;
        }
    }
}