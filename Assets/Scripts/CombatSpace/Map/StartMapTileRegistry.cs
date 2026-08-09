using System;
using System.Collections.Generic;
using UnityEngine;

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

        public int RegisteredTileCount => tilesById.Count;

        private void Awake()
        {
            RebuildRegistry();
        }

        public bool TryGetTile(string tileId,out BattleTile tile)
        {
            if (tilesById.Count == 0)
            {
                RebuildRegistry();
            }

            return tilesById.TryGetValue(tileId, out tile);
        }

        [ContextMenu("Validate Tile IDs")]
        public bool RebuildRegistry()
        {
            tilesById.Clear();

            BattleTile[] tiles = GetComponentsInChildren<BattleTile>(true);

            bool isValid = true;

            foreach (BattleTile tile in tiles)
            {
                if (tile == null || tile.Kind != TileKind.Grass)
                {
                    continue;
                }

                StartMapTileIdentity identity = tile.GetComponent<StartMapTileIdentity>();

                if (identity == null || !identity.HasValidId)
                {
                    Debug.LogError($"[StartMapTileRegistry] {tile.name}에 유효한 타일 ID가 없습니다.",tile);

                    isValid = false;
                    continue;
                }

                if (tilesById.ContainsKey(identity.TileId))
                {
                    Debug.LogError($"[StartMapTileRegistry] 중복 타일 ID입니다:{identity.TileId}",tile);

                    isValid = false;
                    continue;
                }

                tilesById.Add(identity.TileId, tile);
            }

            if (isValid)
            {
                Debug.Log($"[StartMapTileRegistry] {tilesById.Count}개 타일 ID 검증 완료",this);
            }

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
    }
}
