using System;
using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;


namespace NorthLand.Core
{

    /// <summary>
    /// 한 판의 전체 상태를 수집하고 복원 순서를 중앙에서 관리한다.
    /// 개별 시스템의 공개 API를 호출해 저장 DTO를 조립하며,
    /// 개별 시스템은 파일 형식과 직렬화 방식을 알지 않는다.
    /// </summary>
    public sealed class RunSaveManager : MonoBehaviour
    {

        [Tooltip("자원·생산 건물·업그레이드 건물·주민 상태 소유자")]
        [SerializeField]
        private ManagementController management;

        [SerializeField]
        private TerritoryController territory;

        [Tooltip("저장된 타워를 실제 셀에 복원하는 배치 시스템")]
        [SerializeField]
        private TowerPlacer towerPlacer;

        [Tooltip("TowerID로 복원할 수 있는 모든 타워 에셋")]
        [SerializeField]
        private List<TowerAsset> towerAssets = new();

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

        /// <summary>
        /// 현재 확보한 영토 노드 ID를 수집한다.
        /// 맵 구조와 영토 효과 수치는 시드로 다시 생성하므로 저장하지 않는다.
        /// </summary>
        /// <param name="data">
        /// 수집된 영토 상태. 실패하면 null.
        /// </param>
        /// <returns>
        /// 영토 그래프가 준비되어 있고 수집에 성공하면 true.
        /// </returns>
        private bool TryCaptureTerritory(out TerritorySaveData data)
        {
            data = null;

            if (territory == null)
            {
                Debug.LogError("[Save] TerritoryController가 연결되지 않았습니다.",this);

                return false;
            }

            TerritoryGraph graph = territory.Graph;

            if (graph == null)
            {
                Debug.LogError("[Save] 영토 그래프가 준비되지 않았습니다.",this);

                return false;
            }

            data = new TerritorySaveData();

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                TerritoryNode node = graph.Nodes[i];

                if (node == null)
                {
                    Debug.LogError($"[Save] 영토 노드 {i}번 항목이 비어 있습니다.",this);

                    data = null;
                    return false;
                }

                if (graph.IsOwned(node.Id))
                {
                    data.OwnedNodeIds.Add(node.Id);
                }
            }

            return true;
        }

        /// <summary>
        /// 저장된 확보 노드 목록을 현재 영토 그래프에 복원한다.
        /// 영토 그래프는 저장된 시드로 먼저 생성되어 있어야 한다.
        /// </summary>
        /// <param name="data">복원할 영토 상태.</param>
        /// <returns>영토 상태 복원에 성공하면 true.</returns>
        private bool TryRestoreTerritory(TerritorySaveData data)
        {
            if (territory == null)
            {
                Debug.LogError("[Load] TerritoryController가 연결되지 않았습니다.",this);

                return false;
            }

            if (data == null)
            {
                Debug.LogError("[Load] 영토 세이브 데이터가 없습니다.",this);

                return false;
            }

            if (data.OwnedNodeIds == null)
            {
                Debug.LogError("[Load] 확보 영토 노드 목록이 없습니다.",this);

                return false;
            }

            TerritoryGraph graph = territory.Graph;

            if (graph == null)
            {
                Debug.LogError("[Load] 영토 그래프가 준비되지 않았습니다.",this);

                return false;
            }

            if (!graph.TryRestoreOwnedNodes(data.OwnedNodeIds))
            {
                Debug.LogError("[Load] 영토 상태 복원에 실패했습니다.",this);

                return false;
            }

            return true;
        }

        /// <summary>
        /// 현재 설치된 모든 타워의 ID와 기준 셀 좌표를 수집한다.
        /// 타워의 능력치와 프리팹 정보는 TowerAsset에서 다시 조회한다.
        /// </summary>
        /// <param name="data">
        /// 수집된 타워 목록. 실패하면 null.
        /// </param>
        /// <returns>
        /// 모든 활성 타워의 저장 데이터 수집에 성공하면 true.
        /// </returns>
        private bool TryCaptureTowers(out List<TowerSaveData> data)
        {
            data = null;

            var captured = new List<TowerSaveData>(Tower.Active.Count);

            for (int i = 0; i < Tower.Active.Count; i++)
            {
                Tower tower = Tower.Active[i];

                if (tower == null)
                {
                    Debug.LogError($"[Save] 활성 타워 {i}번 항목이 비어 있습니다.",this);

                    return false;
                }

                if (tower.Asset == null)
                {
                    Debug.LogError($"[Save] 타워 {tower.name}에 TowerAsset이 없습니다.",tower);

                    return false;
                }

                if (string.IsNullOrEmpty(tower.Asset.TowerID))
                {
                    Debug.LogError($"[Save] 타워 {tower.name}의 TowerID가 비어 있습니다.",tower);

                    return false;
                }

                if (!tower.TryGetComponent(
                        out TowerFootprint footprint))
                {
                    Debug.LogError($"[Save] 타워 {tower.name}에 TowerFootprint가 없습니다.",tower);

                    return false;
                }

                if (!footprint.HasAnchorCell)
                {
                    Debug.LogError($"[Save] 타워 {tower.name}의 기준 셀 좌표가 기록되지 않았습니다.",tower);

                    return false;
                }

                Vector2Int cell = footprint.AnchorCell;

                captured.Add(new TowerSaveData
                {
                    TowerId = tower.Asset.TowerID,
                    CellX = cell.x,
                    CellZ = cell.y
                });
            }

            data = captured;
            return true;
        }

        /// <summary>
        /// 인스펙터에 등록된 타워 에셋을 TowerID 기반 조회표로 만든다.
        /// 빈 ID와 중복 ID는 복원 결과를 모호하게 하므로 거부한다.
        /// </summary>
        /// <param name="lookup">
        /// TowerID를 키로 사용하는 타워 에셋 조회표.
        /// 실패하면 null.
        /// </param>
        /// <returns>조회표 생성에 성공하면 true.</returns>
        private bool TryBuildTowerAssetLookup(out Dictionary<string, TowerAsset> lookup)
        {
            lookup = null;

            if (towerAssets == null)
            {
                Debug.LogError("[Load] 타워 에셋 목록이 없습니다.",this);

                return false;
            }

            var result = new Dictionary<string, TowerAsset>(StringComparer.Ordinal);

            for (int i = 0; i < towerAssets.Count; i++)
            {
                TowerAsset asset = towerAssets[i];

                if (asset == null)
                {
                    Debug.LogError($"[Load] 타워 에셋 목록의 {i}번 항목이 비어 있습니다.",this);

                    return false;
                }

                if (string.IsNullOrEmpty(asset.TowerID))
                {
                    Debug.LogError($"[Load] 타워 에셋 {asset.name}의 TowerID가 비어 있습니다.",asset);

                    return false;
                }

                if (result.ContainsKey(asset.TowerID))
                {
                    Debug.LogError($"[Load] 중복된 TowerID가 등록되어 있습니다: {asset.TowerID}",this);

                    return false;
                }

                result.Add(asset.TowerID,asset);
            }

            lookup = result;
            return true;
        }


        /// <summary>
        /// 저장된 타워 목록을 현재 전투 맵에 복원한다.
        /// 모든 데이터를 먼저 검증하고, 중간 실패 시 이번 복원에서 생성한 타워를 제거한다.
        /// </summary>
        /// <param name="data">복원할 타워 저장 데이터 목록.</param>
        /// <returns>모든 타워 복원에 성공하면 true.</returns>
        private bool TryRestoreTowers(List<TowerSaveData> data)
        {
            if (towerPlacer == null)
            {
                Debug.LogError("[Load] TowerPlacer가 연결되지 않았습니다.",this);

                return false;
            }

            if (data == null)
            {
                Debug.LogError("[Load] 타워 세이브 데이터 목록이 없습니다.",this);

                return false;
            }

            if (Tower.Active.Count > 0)
            {
                Debug.LogError($"[Load] 기존 활성 타워가 남아 있어 복원할 수 없습니다: {Tower.Active.Count}개",this);

                return false;
            }

            // 타워가 없는 저장 상태도 정상이다.
            if (data.Count == 0)
                return true;

            if (!TryBuildTowerAssetLookup(out Dictionary<string, TowerAsset> lookup))
            {
                return false;
            }

            var restoredAnchorCells = new HashSet<Vector2Int>();

            // 실제 타워를 만들기 전에 저장 데이터 전체를 검증한다.
            for (int i = 0; i < data.Count; i++)
            {
                TowerSaveData savedTower = data[i];

                if (savedTower == null)
                {
                    Debug.LogError($"[Load] 타워 데이터 {i}번 항목이 비어 있습니다.",this);

                    return false;
                }

                if (string.IsNullOrEmpty(savedTower.TowerId))
                {
                    Debug.LogError($"[Load] 타워 데이터 {i}번의 TowerID가 비어 있습니다.",this);

                    return false;
                }

                if (!lookup.TryGetValue(savedTower.TowerId,out TowerAsset asset))
                {
                    Debug.LogError($"[Load] TowerAsset을 찾을 수 없습니다: {savedTower.TowerId}",this);

                    return false;
                }

                Vector2Int anchorCell =new Vector2Int(savedTower.CellX,savedTower.CellZ);

                if (!restoredAnchorCells.Add(anchorCell))
                {
                    Debug.LogError($"[Load] 중복된 타워 기준 셀이 있습니다: {anchorCell}",this);

                    return false;
                }

                if (asset.Data == null)
                {
                    asset.Data =DataTableManager.Get<TowerTable>("TowerTable")?.Get(asset.TowerID);
                }

                if (asset.Data == null)
                {
                    Debug.LogError($"[Load] TowerData를 찾을 수 없습니다: {asset.TowerID}",asset);

                    return false;
                }

                if (asset.TowerPrefab == null)
                {
                    Debug.LogError($"[Load] TowerPrefab이 없습니다: {asset.TowerID}",asset);

                    return false;
                }

                if (!asset.TowerPrefab.TryGetComponent<Tower>(out _))
                {
                    Debug.LogError($"[Load] TowerPrefab에 Tower 컴포넌트가 없습니다: {asset.TowerID}",asset.TowerPrefab);

                    return false;
                }
            }

            // 검증이 끝난 뒤 실제 타워를 생성한다.
            var restoredTowers = new List<Tower>(data.Count);

            for (int i = 0; i < data.Count; i++)
            {
                TowerSaveData savedTower = data[i];
                TowerAsset asset = lookup[savedTower.TowerId];

                Vector2Int anchorCell = new Vector2Int(savedTower.CellX,savedTower.CellZ);

                if (!towerPlacer.TryRestoreTower(asset,anchorCell,out Tower restoredTower))
                {
                    Debug.LogError($"[Load] 타워 복원에 실패했습니다: {savedTower.TowerId}, 셀={anchorCell}",this);

                    // 이번 로드에서 이미 생성한 타워만 되돌린다.
                    for (int j = restoredTowers.Count - 1;j >= 0;j--)
                    {
                        Tower tower = restoredTowers[j];

                        if (tower == null)
                            continue;

                        tower.gameObject.SetActive(false);
                        Destroy(tower.gameObject);
                    }

                    return false;
                }

                restoredTowers.Add(restoredTower);
            }

            return true;
        }
    }


}
    

        
    
