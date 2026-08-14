using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
#if UNITY_EDITOR
using UnityEditor;
#endif


namespace CombatSpace
{
    public sealed class StartMapTileRegistry : MonoBehaviour
    {
        [SerializeField]
        private string idPrefix = "StartTile_";

        private readonly Dictionary<string, BattleTile> tilesById = new Dictionary<string, BattleTile>(StringComparer.Ordinal);

        private bool isRegistryBuilt;

        public int RegisteredTileCount => tilesById.Count;

        private readonly HashSet<string> duplicateIds = new HashSet<string>(StringComparer.Ordinal);

        private void Awake()
        {
            RebuildRegistry();
        }

        public bool TryGetTile(string tileId, out BattleTile tile)
        {
            tile = null;

            if (string.IsNullOrWhiteSpace(tileId))
            {
                return false;
            }

            if (!isRegistryBuilt)
            {
                RebuildRegistry();
            }

            return tilesById.TryGetValue(tileId, out tile);
        }

        [ContextMenu("Validate Tile IDs")]
        public bool RebuildRegistry()
        {
            isRegistryBuilt = false;
            tilesById.Clear();
            duplicateIds.Clear();

            BattleTile[] tiles = GetComponentsInChildren<BattleTile>(true);

            bool isValid = true;

            foreach (BattleTile tile in tiles)
            {

                // 타워 설치 가능 지형 기준은 TowerPlacer.IsBuildable과 동기화되어야 한다.
                // Occupied는 런타임 상태이므로 레지스트리 등록 조건에는 포함하지 않는다.
                // 건설 가능 지형이 확장되면 두 조건을 공통 프로퍼티로 통합한다.
                if (tile == null || tile.Kind != TileKind.Grass)
                {
                    continue;
                }

                StartMapTileIdentity identity = tile.GetComponent<StartMapTileIdentity>();

                if (identity == null || !identity.HasValidId)
                {
                    Debug.LogWarning($"[StartMapTileRegistry] {tile.name}에 유효한 타일 ID가 없습니다.",tile);

                    isValid = false;
                    continue;
                }

                string tileId = identity.TileId;

                if (duplicateIds.Contains(tileId) || !tilesById.TryAdd(tileId, tile))
                {
                    duplicateIds.Add(tileId);

                    // 중복 ID는 어느 타일을 가리키는지 모호하므로 조회할 수 없게 한다.
                    tilesById.Remove(tileId);

                    Debug.LogWarning($"[StartMapTileRegistry] 중복 타일 ID입니다: {tileId}",tile);

                    isValid = false;
                    continue;
                }
            }

            if (isValid)
            {
                Debug.Log($"[StartMapTileRegistry] {tilesById.Count}개 타일 ID 검증 완료",this);
            }

            isRegistryBuilt = true;
            return isValid;
        }

#if UNITY_EDITOR
        [ContextMenu("Generate Missing Tile IDs")]
        private void GenerateMissingTileIds()
        {
            BattleTile[] tiles = GetComponentsInChildren<BattleTile>(true);

            Array.Sort(
                tiles,
                (left, right) =>
                {
                    int zCompare = left.transform.localPosition.z.CompareTo(right.transform.localPosition.z);

                    if (zCompare != 0)
                    {
                        return zCompare;
                    }

                    return left.transform.localPosition.x.CompareTo(right.transform.localPosition.x);
                });

            var usedIds = new HashSet<string>(StringComparer.Ordinal);

            int nextNumber = 0;

            // 기존 ID는 유지한다.
            foreach (BattleTile tile in tiles)
            {
                if (tile == null || tile.Kind != TileKind.Grass)
                {
                    continue;
                }

                StartMapTileIdentity identity = tile.GetComponent<StartMapTileIdentity>();

                if (identity == null || !identity.HasValidId)
                {
                    continue;
                }

                usedIds.Add(identity.TileId);

                if (identity.TileId.StartsWith(idPrefix,StringComparison.Ordinal) &&int.TryParse(identity.TileId.Substring(idPrefix.Length),out int number))
                {
                    nextNumber = Mathf.Max(nextNumber, number + 1);
                }
            }

            int generatedCount = 0;

            foreach (BattleTile tile in tiles)
            {
                if (tile == null || tile.Kind != TileKind.Grass)
                {
                    continue;
                }

                StartMapTileIdentity identity = tile.GetComponent<StartMapTileIdentity>();

                if (identity == null)
                {
                    identity =Undo.AddComponent<StartMapTileIdentity>(tile.gameObject);
                }

                if (identity.HasValidId)
                {
                    continue;
                }

                string newId;

                do
                {
                    newId = $"{idPrefix}{nextNumber:D3}";

                    nextNumber++;
                }
                while (usedIds.Contains(newId));

                SerializedObject serializedIdentity = new SerializedObject(identity);

                SerializedProperty tileIdProperty = serializedIdentity.FindProperty("tileId");

                tileIdProperty.stringValue = newId;
                serializedIdentity.ApplyModifiedProperties();

                PrefabUtility.RecordPrefabInstancePropertyModifications(identity);

                EditorUtility.SetDirty(identity);

                usedIds.Add(newId);
                generatedCount++;
            }

            EditorUtility.SetDirty(this);

            Debug.Log($"[StartMapTileRegistry] {generatedCount}개의 누락된 타일 ID를 생성했습니다.",this);

            RebuildRegistry();
        }
#endif




        public bool TryGetTileId(BattleTile tile, out string tileId)
        {
            tileId = string.Empty;

            if (tile == null)
            {
                return false;
            }

            if (!isRegistryBuilt)
            {
                RebuildRegistry();
            }

            foreach (KeyValuePair<string, BattleTile> pair in tilesById)
            {
                if (pair.Value != tile)
                {
                    continue;
                }

                tileId = pair.Key;
                return true;
            }

            return false;
        }
    }
}
