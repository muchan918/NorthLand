using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Core
{
    public sealed partial class RunSaveManager
    {
        /// <summary>
        /// 현재 경영 상태를 ID 기반 저장 데이터로 수집한다.
        /// 건물은 배열 인덱스가 아니라 BuildingID로 기록한다.
        /// </summary>
        /// <param name="data">
        /// 수집에 성공한 경영 상태. 실패하면 null.
        /// </param>
        /// <returns>
        /// 필수 시스템이 준비되어 있고 수집에 성공하면 true.
        /// </returns>
        private bool TryCaptureManagement(out ManagementSaveData data)
        {
            data = null;

            if (management == null)
            {
                Debug.LogError("[Save] ManagementController가 연결되지 않았습니다.", this);

                return false;
            }

            data = new ManagementSaveData();

            // 자원 보유량 수집
            foreach (ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))
            {
                data.Resources.Add(new ResourceSaveData
                {
                    Kind = kind,
                    Amount = management.ResourceCount(kind)
                });
            }

            // 생산 건물 상태 수집
            for (int i = 0; i < management.LineCount; i++)
            {
                string buildingId = management.LineBuildingId(i);

                if (string.IsNullOrEmpty(buildingId))
                {
                    Debug.LogWarning($"[Save] 생산 라인 {i}의 BuildingID가 비어 있어 저장할 수 없습니다.", this);

                    data = null;
                    return false;
                }

                data.ProductionBuildings.Add(new ProductionBuildingSaveData
                {
                    BuildingId = buildingId,
                    Level = management.LineLevel(i),
                    Villagers = management.LineVillagers(i)
                });
            }

            // 업그레이드 건물 상태 수집
            for (int i = 0; i < management.UpgradeBuildingCount; i++)
            {
                string buildingId = management.UpgradeBuildingId(i);

                if (string.IsNullOrEmpty(buildingId))
                {
                    Debug.LogWarning($"[Save] 업그레이드 건물 {i}의 BuildingID가 비어 있어 저장할 수 없습니다.", this);

                    data = null;
                    return false;
                }


                data.UpgradeBuildings.Add(new UpgradeBuildingSaveData
                {
                    BuildingId = buildingId,
                    Level = management.UpgradeBuildingLevel(i)
                });

            }

            // 증축한 주민 수
            data.BonusVillagers = management.VillagerGrowthCount;

            return true;
        }

        /// <summary>
        /// 저장된 경영 상태를 현재 런타임 시스템에 복원한다.
        /// 일반 구매·업그레이드 경로를 통하지 않으므로 비용을 다시 차감하지 않는다.
        /// </summary>
        /// <param name="data">복원할 경영 상태.</param>
        /// <returns>
        /// 입력 검증과 전체 복원에 성공하면 true.
        /// </returns>
        private bool TryRestoreManagement(ManagementSaveData data)
        {
            if (management == null)
            {
                Debug.LogError("[Load] ManagementController가 연결되지 않았습니다.",this);

                return false;
            }

            if (data == null)
            {
                Debug.LogError("[Load] 경영 세이브 데이터가 없습니다.",this);

                return false;
            }

            if (data.Resources == null)
            {
                Debug.LogError("[Load] 자원 목록이 없습니다.",this);

                return false;
            }

            if (data.UpgradeBuildings == null)
            {
                Debug.LogError("[Load] 업그레이드 건물 목록이 없습니다.", this);

                return false;
            }

            if (data.ProductionBuildings == null)
            {
                Debug.LogError("[Load] 생산 건물 목록이 없습니다.", this);

                return false;
            }

            if (data.BonusVillagers < 0)
            {
                Debug.LogError($"[Load] 증축 주민 수가 음수입니다: {data.BonusVillagers}", this);

                return false;
            }


            // 자원 데이터 전체를 먼저 검증한다.
            // 검증 중에는 런타임 상태를 변경하지 않는다.
            var restoredKinds = new HashSet<ResourceKind>();

            for (int i = 0; i < data.Resources.Count; i++)
            {
                ResourceSaveData resource = data.Resources[i];

                if (resource == null)
                {
                    Debug.LogError($"[Load] 자원 데이터 {i}번 항목이 비어 있습니다.", this);

                    return false;
                }

                if (!Enum.IsDefined(typeof(ResourceKind), resource.Kind))
                {
                    Debug.LogError($"[Load] 알 수 없는 자원 종류입니다: {(int)resource.Kind}", this);

                    return false;
                }

                if (resource.Amount < 0)
                {
                    Debug.LogError($"[Load] 자원 보유량이 음수입니다: {resource.Kind}={resource.Amount}", this);

                    return false;
                }

                if (!restoredKinds.Add(resource.Kind))
                {
                    Debug.LogError($"[Load] 중복된 자원 종류가 있습니다: {resource.Kind}", this);

                    return false;
                }
            }

            // v1 세이브는 모든 자원 종류를 담는 전체 스냅샷이다.
            // 하나라도 없으면 일부 자원이 게임 초기값으로 남으므로 로드를 거부한다.
            foreach (ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))
            {
                if (!restoredKinds.Contains(kind))
                {
                    Debug.LogError($"[Load] 저장 데이터에 자원이 누락됐습니다: {kind}", this);

                    return false;
                }
            }

            // 업그레이드 건물 데이터 전체 검증
            var restoredUpgradeIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < data.UpgradeBuildings.Count; i++)
            {
                UpgradeBuildingSaveData building = data.UpgradeBuildings[i];

                if (building == null)
                {
                    Debug.LogError($"[Load] 업그레이드 건물 데이터 {i}번 항목이 비어 있습니다.", this);

                    return false;
                }

                if (string.IsNullOrEmpty(building.BuildingId))
                {
                    Debug.LogError($"[Load] 업그레이드 건물 데이터 {i}번의 BuildingID가 비어 있습니다.", this);

                    return false;
                }

                if (!restoredUpgradeIds.Add(building.BuildingId))
                {
                    Debug.LogError($"[Load] 중복된 업그레이드 건물 ID가 있습니다: {building.BuildingId}", this);

                    return false;
                }

                if (!management.CanRestoreUpgradeBuilding(building.BuildingId, building.Level))
                {
                    Debug.LogError($"[Load] 업그레이드 건물 데이터를 복원할 수 없습니다: {building.BuildingId}", this);

                    return false;
                }

            }

            if (restoredUpgradeIds.Count != management.UpgradeBuildingCount)
            {
                Debug.LogError(
                    $"[Load] 업그레이드 건물 수가 일치하지 않습니다. 저장={restoredUpgradeIds.Count}, 현재={management.UpgradeBuildingCount}",
                    this);

                return false;
            }


            // 생산 건물 데이터 전체 검증
            var restoredProductionIds =new HashSet<string>(StringComparer.Ordinal);

                long assignedVillagers = 0;


                for (int i = 0;i < data.ProductionBuildings.Count;i++)
                {
                    ProductionBuildingSaveData building = data.ProductionBuildings[i];

                    if (building == null)
                    {
                        Debug.LogError($"[Load] 생산 건물 데이터 {i}번 항목이 비어 있습니다.",this);

                        return false;
                    }

                    if (string.IsNullOrEmpty(building.BuildingId))
                    {
                        Debug.LogError($"[Load] 생산 건물 데이터 {i}번의 BuildingID가 비어 있습니다.",this);

                        return false;
                    }

                    if (!restoredProductionIds.Add(building.BuildingId))
                    {
                        Debug.LogError($"[Load] 중복된 생산 건물 ID가 있습니다: {building.BuildingId}",this);

                        return false;
                    }

                    if (!management.CanRestoreProductionLine(building.BuildingId,building.Level,building.Villagers))
                    {
                        Debug.LogError($"[Load] 생산 건물 데이터를 복원할 수 없습니다: {building.BuildingId}",this);

                        return false;
                    }


                      assignedVillagers += building.Villagers;
                }

            if (restoredProductionIds.Count != management.LineCount)
            {
                Debug.LogError(
                    $"[Load] 생산 건물 수가 일치하지 않습니다. 저장={restoredProductionIds.Count}, 현재={management.LineCount}",
                    this);

                return false;
            }

            // 주민 데이터 전체 검증

            long restoredMaxVillagers =(long)management.BaseMaxVillagers + data.BonusVillagers;

            if (assignedVillagers >restoredMaxVillagers)
            {
                Debug.LogError($"[Load] 배치 주민 수가 최대 주민 수를 초과합니다: 배치={assignedVillagers}, 최대={restoredMaxVillagers}",this);

                return false;
            }

            // 검증이 전부 끝난 뒤 실제 지갑 상태를 변경한다.
            for (int i = 0; i < data.Resources.Count; i++)
            {
                ResourceSaveData resource = data.Resources[i];

                if (!management.TryRestoreResource(resource.Kind,resource.Amount))
                {
                    Debug.LogError($"[Load] 자원 복원 실패: {resource.Kind}={resource.Amount}",this);

                    return false;
                }
            }


            // 업그레이드 건물 상태 적용
            for (int i = 0;i < data.UpgradeBuildings.Count;i++)
            {
                UpgradeBuildingSaveData building = data.UpgradeBuildings[i];

                if (!management.TryRestoreUpgradeBuilding(building.BuildingId,building.Level))
                {
                    Debug.LogError($"[Load] 업그레이드 건물 복원 실패: {building.BuildingId}",this);

                    return false;
                }
            }
            
            // 증축 주민 수 적용
            if (!management.TryRestoreBonusVillagers(data.BonusVillagers))
            {
                Debug.LogError($"[Load] 증축 주민 수 복원 실패: {data.BonusVillagers}",this);

                return false;
            }

            // 생산 건물 상태 적용
            for (int i = 0; i < data.ProductionBuildings.Count; i++)
            {
                ProductionBuildingSaveData building = data.ProductionBuildings[i];

                if (!management.TryRestoreProductionLine(building.BuildingId, building.Level, building.Villagers))
                {
                    Debug.LogError($"[Load] 생산 건물 복원 실패: {building.BuildingId}", this);

                    return false;
                }
            }



            return true;
        }
    }
}

