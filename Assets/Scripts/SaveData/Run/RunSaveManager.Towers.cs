using System;
using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;


namespace NorthLand.Core
{
    public sealed partial class RunSaveManager
    {
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
                    Debug.LogError($"[Save] 활성 타워 {i}번 항목이 비어 있습니다.", this);

                    return false;
                }

                if (tower.Asset == null)
                {
                    Debug.LogError($"[Save] 타워 {tower.name}에 TowerAsset이 없습니다.", tower);

                    return false;
                }

                if (string.IsNullOrEmpty(tower.Asset.TowerID))
                {
                    Debug.LogError($"[Save] 타워 {tower.name}의 TowerID가 비어 있습니다.", tower);

                    return false;
                }

                if (!tower.TryGetComponent(
                        out TowerFootprint footprint))
                {
                    Debug.LogError($"[Save] 타워 {tower.name}에 TowerFootprint가 없습니다.", tower);

                    return false;
                }

                if (!footprint.HasAnchorTile &&!footprint.HasAnchorCell)
                {
                    Debug.LogError($"[Save] 타워 {tower.name}의 기준 위치가 기록되지 않았습니다.",tower);

                    return false;
                }

                if (!footprint.HasAnchorTile)
                {
                    Debug.LogWarning($"[Save] 타워 {tower.name}의 기준 타일 참조가 없어 셀 좌표로 저장합니다.",tower);
                }

                BattleTile anchorTile = footprint.AnchorTile;

                var savedTower = new TowerSaveData
                {
                    TowerId = tower.Asset.TowerID
                };

                if (startMapTileRegistry != null &&startMapTileRegistry.TryGetTileId(anchorTile,out string startTileId))
                {
                    savedTower.MapArea = MapArea.StartMap;
                    savedTower.StartTileId = startTileId;
                }
                else
                {
                    if (!footprint.HasAnchorCell)
                    {
                        Debug.LogError($"[Save] 타워 {tower.name}의 기준 셀 좌표가 기록되지 않았습니다.", tower);

                        return false;
                    }

                    Vector2Int cell = footprint.AnchorCell;

                    savedTower.MapArea = MapArea.CombatMap;
                    savedTower.CellX = cell.x;
                    savedTower.CellZ = cell.y;
                    savedTower.StartTileId = null;
                }

                captured.Add(savedTower);
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

            TowerAsset[] loadedAssets = Resources.LoadAll<TowerAsset>("ScriptableObjects/Towers");

            if (loadedAssets == null || loadedAssets.Length == 0)
            {
                Debug.LogError("[Load] Resources에서 타워 에셋을 찾을 수 없습니다.",this);

                return false;
            }

            var result =new Dictionary<string, TowerAsset>(loadedAssets.Length,StringComparer.Ordinal);

            for (int i = 0; i < loadedAssets.Length; i++)
            {
                TowerAsset asset = loadedAssets[i];

                if (asset == null)
                {
                    Debug.LogError($"[Load] 로드된 타워 에셋 {i}번 항목이 비어 있습니다.",this);

                    return false;
                }

                if (string.IsNullOrWhiteSpace(asset.TowerID))
                {
                    Debug.LogError($"[Load] 타워 에셋 {asset.name}의 TowerID가 비어 있습니다.",asset);

                    return false;
                }

                if (!result.TryAdd(asset.TowerID, asset))
                {
                    Debug.LogError($"[Load] 중복된 TowerID가 존재합니다: {asset.TowerID}",asset);

                    return false;
                }
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
                Debug.LogError("[Load] TowerPlacer가 연결되지 않았습니다.", this);

                return false;
            }

            if (data == null)
            {
                Debug.LogError("[Load] 타워 세이브 데이터 목록이 없습니다.", this);

                return false;
            }

            if (Tower.Active.Count > 0)
            {
                Debug.LogError($"[Load] 기존 활성 타워가 남아 있어 복원할 수 없습니다: {Tower.Active.Count}개", this);

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
            var restoredStartTileIds = new HashSet<string>(StringComparer.Ordinal);
            bool startMapRegistryValidated = false;

            // 실제 타워를 만들기 전에 저장 데이터 전체를 검증한다.
            for (int i = 0; i < data.Count; i++)
            {
                TowerSaveData savedTower = data[i];

                if (savedTower == null)
                {
                    Debug.LogError($"[Load] 타워 데이터 {i}번 항목이 비어 있습니다.", this);

                    return false;
                }

                if (string.IsNullOrEmpty(savedTower.TowerId))
                {
                    Debug.LogError($"[Load] 타워 데이터 {i}번의 TowerID가 비어 있습니다.", this);

                    return false;
                }

                if (!lookup.TryGetValue(savedTower.TowerId, out TowerAsset asset))
                {
                    Debug.LogError($"[Load] TowerAsset을 찾을 수 없습니다: {savedTower.TowerId}", this);

                    return false;
                }

                switch (savedTower.MapArea)
                {
                    case MapArea.CombatMap:
                        {
                            Vector2Int anchorCell =
                                new Vector2Int(savedTower.CellX, savedTower.CellZ);

                            if (!restoredAnchorCells.Add(anchorCell))
                            {
                                Debug.LogError(
                                    $"[Load] 중복된 자동맵 타워 기준 셀이 있습니다: {anchorCell}",
                                    this);

                                return false;
                            }

                            break;
                        }

                    case MapArea.StartMap:
                        {
                            if (startMapTileRegistry == null)
                            {
                                Debug.LogError(
                                    "[Load] StartMapTileRegistry가 연결되지 않았습니다.",
                                    this);

                                return false;
                            }
                            if (!startMapRegistryValidated)
                            {
                                // 전체 타일의 ID 누락·중복은 경고만 남긴다.
                                // 실제 저장 데이터가 참조하는 ID는 아래 TryGetTile에서 개별 검증한다.
                                startMapTileRegistry.RebuildRegistry();
                                startMapRegistryValidated = true;
                            }

                            if (string.IsNullOrWhiteSpace(savedTower.StartTileId))
                            {
                                Debug.LogError(
                                    $"[Load] 스타트맵 타워 {i}번의 타일 ID가 비어 있습니다.",
                                    this);

                                return false;
                            }

                            if (!restoredStartTileIds.Add(savedTower.StartTileId))
                            {
                                Debug.LogError(
                                    $"[Load] 중복된 스타트맵 타일 ID가 있습니다: {savedTower.StartTileId}",
                                    this);

                                return false;
                            }

                            if (!startMapTileRegistry.TryGetTile(
                                    savedTower.StartTileId,
                                    out _))
                            {
                                Debug.LogError(
                                    $"[Load] 스타트맵 타일을 찾을 수 없습니다: {savedTower.StartTileId}",
                                    this);

                                return false;
                            }

                            break;
                        }

                    default:
                        Debug.LogError(
                            $"[Load] 알 수 없는 타워 맵 영역입니다: {(int)savedTower.MapArea}",
                            this);

                        return false;
                }

                if (asset.Data == null)
                {
                    asset.Data = DataTableManager.Get<TowerTable>("TowerTable")?.Get(asset.TowerID);
                }

                if (asset.Data == null)
                {
                    Debug.LogError($"[Load] TowerData를 찾을 수 없습니다: {asset.TowerID}", asset);

                    return false;
                }

                if (asset.TowerPrefab == null)
                {
                    Debug.LogError($"[Load] TowerPrefab이 없습니다: {asset.TowerID}", asset);

                    return false;
                }

                if (!asset.TowerPrefab.TryGetComponent<Tower>(out _))
                {
                    Debug.LogError($"[Load] TowerPrefab에 Tower 컴포넌트가 없습니다: {asset.TowerID}", asset.TowerPrefab);

                    return false;
                }
            }

            // 검증이 끝난 뒤 실제 타워를 생성한다.
            var restoredTowers = new List<Tower>(data.Count);

            for (int i = 0; i < data.Count; i++)
            {
                TowerSaveData savedTower = data[i];
                TowerAsset asset = lookup[savedTower.TowerId];

                Tower restoredTower = null;
                bool restoreSucceeded;
                string location;

                if (savedTower.MapArea == MapArea.StartMap)
                {
                    restoreSucceeded =
                        startMapTileRegistry.TryGetTile(
                            savedTower.StartTileId,
                            out BattleTile anchorTile) &&
                        towerPlacer.TryRestoreTower(
                            asset,
                            anchorTile,
                            out restoredTower);

                    location = $"스타트 타일={savedTower.StartTileId}";
                }
                else
                {
                    Vector2Int anchorCell =
                        new Vector2Int(savedTower.CellX, savedTower.CellZ);

                    restoreSucceeded = towerPlacer.TryRestoreTower(
                        asset,
                        anchorCell,
                        out restoredTower);

                    location = $"셀={anchorCell}";
                }

                if (!restoreSucceeded)
                {
                    Debug.LogError(
                        $"[Load] 타워 복원에 실패했습니다: {savedTower.TowerId}, {location}",
                        this);

                    // 이번 로드에서 이미 생성한 타워만 되돌린다.
                    for (int j = restoredTowers.Count - 1; j >= 0; j--)
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

