using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Core
{

    public sealed partial class RunSaveManager
    {
        [Tooltip("현재 Run의 마스터 시드와 시스템별 최종 사용 시드를 제공하는 부트스트래퍼")]
        [SerializeField]
        private RunBootstrapper runBootstrapper;

        /// <summary>
        /// 현재 Run에서 실제로 사용된 시드 정보를 저장 데이터로 복사한다.
        /// 런타임 객체를 그대로 참조하지 않고 새로운 DTO를 만든다.
        /// </summary>
        private bool TryCaptureSeed(out RunSeedData data)
        {
            data = null;

            if (runBootstrapper == null)
            {
                Debug.LogError("[Save] RunBootstrapper가 연결되지 않았습니다.",this);

                return false;
            }

            RunSeedData current = runBootstrapper.SeedData;

            if (current == null)
            {
                Debug.LogError("[Save] Run 시드가 초기화되지 않았습니다.",this);

                return false;
            }

            if (current.MasterSeed <= 0)
            {
                Debug.LogError($"[Save] 마스터 시드가 유효하지 않습니다: {current.MasterSeed}",this);

                return false;
            }

            if (current.CombatMapUsedSeed == 0)
            {
                Debug.LogError("[Save] 전투 맵의 최종 사용 시드가 기록되지 않았습니다.",this);

                return false;
            }

            if (current.TerritoryUsedSeed == 0)
            {
                Debug.LogError("[Save] 영토의 최종 사용 시드가 기록되지 않았습니다.",this);

                return false;
            }

            data = new RunSeedData
            {
                SeedVersion = current.SeedVersion,
                MasterSeed = current.MasterSeed,
                CombatMapRequestedSeed = current.CombatMapRequestedSeed,
                CombatMapUsedSeed = current.CombatMapUsedSeed,
                TerritoryRequestedSeed = current.TerritoryRequestedSeed,
                TerritoryUsedSeed = current.TerritoryUsedSeed
            };

            return true;
        }

        /// <summary>
        /// 현재 한 판의 전체 상태를 하나의 RunData로 수집한다.
        /// 어느 영역이라도 수집에 실패하면 불완전한 데이터는 반환하지 않는다.
        /// </summary>
        private bool TryCaptureRunData(out RunData data)
        {
            data = null;

            if (!TryCaptureProgress(out ProgressSaveData progress))
                return false;

            if (!TryCaptureManagement(out ManagementSaveData managementData))
                return false;

            if (!TryCaptureTerritory(out TerritorySaveData territoryData))
                return false;

            if (!TryCaptureTowers(out List<TowerSaveData> towers))
                return false;

            if (!TryCapturePlayerBase(out BaseSaveData playerBaseData))
                return false;

            if (!TryCaptureRewardEffects(
                    out List<RewardEffectSaveData> rewardEffects))
            {
                return false;
            }

            if (!TryCaptureSeed(out RunSeedData seedData))
                return false;

            data = new RunData
            {
                SeedData = seedData,
                Progress = progress,
                Management = managementData,
                Territory = territoryData,
                Towers = towers,
                RewardEffects = rewardEffects,
                PlayerBase = playerBaseData
            };

            return true;
        }

        /// <summary>
        /// 저장된 전체 Run 상태를 의존 순서에 맞춰 런타임 시스템에 복원한다.
        /// 시드 주입과 월드 생성은 이 메서드 호출 전에 완료되어 있어야 한다.
        /// </summary>
        private bool TryRestoreRunData(RunData data)
        {
            if (data == null)
            {
                Debug.LogError("[Load] 복원할 RunData가 없습니다.",this);

                return false;
            }

            if (runBootstrapper == null)
            {
                Debug.LogError("[Load] RunBootstrapper가 연결되지 않았습니다.",this);

                return false;
            }

            if (runBootstrapper.RunData != data)
            {
                Debug.LogError("[Load] 저장된 RunData가 RunBootstrapper에 먼저 주입되지 않았습니다.",this);

                return false;
            }

            // 저장 시드로 생성된 영토 그래프에 확보 상태를 적용한다.
            if (!TryRestoreTerritory(data.Territory))
                return false;

            // 경영 상태는 영토와 맵이 준비된 뒤 복원한다.
            if (!TryRestoreManagement(data.Management))
                return false;

            // 타워는 전투 맵의 셀과 타일 버프가 준비된 뒤 복원한다.
            if (!TryRestoreTowers(data.Towers))
                return false;

            if (!TryRestorePlayerBase(data.PlayerBase))
                return false;

            if (!TryRestoreRewardEffects(data.RewardEffects))
                return false;

            // 페이즈 복원은 모든 시스템 복원이 끝난 뒤 마지막에 수행한다.
            if (!TryRestoreProgress(data.Progress))
                return false;

            return true;
        }

    }
}