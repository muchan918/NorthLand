using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    // 논리 맵 데이터를 실제 타일 GameObject로 생성
    public sealed class CombatMapTileSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private CombatMapGenerator mapGenerator;

        [SerializeField]
        private Transform tileRoot;

        [Header("Prefabs")]
        [SerializeField]
        private GameObject roadTilePrefab;

        [SerializeField]
        private GameObject grassTilePrefab;

        [SerializeField]
        private GameObject waterTilePrefab;

        [Header("Tile")]
        [SerializeField]
        [Min(0.01f)]
        private float tileSize = 1f;

        [SerializeField]
        private float tileHeight = 0f;

        private readonly Dictionary<Vector2Int,CombatMapTileView> spawnedTiles = new Dictionary<Vector2Int, CombatMapTileView>();

        public int SpawnedTileCount =>spawnedTiles.Count;

        // 생성된 타일을 좌표로 검색
        public bool TryGetTileView(Vector2Int position,out CombatMapTileView tileView)
        {
            return spawnedTiles.TryGetValue(position,out tileView);
        }

        [ContextMenu("Spawn Map Tiles")]
        public void SpawnTiles()
        {
            if (mapGenerator == null ||mapGenerator.CurrentMap == null)
            {
                Debug.LogError("먼저 전투맵 데이터를 생성해야 합니다.",this);

                return;
            }

            if (!ValidatePrefabs())
            {
                return;
            }

            ClearTiles();

            CombatMapData map =mapGenerator.CurrentMap;

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position =new Vector2Int(x, y);

                    CombatTileData tileData =map.GetTile(position);

                    if (tileData.Type ==CombatTileType.Empty)
                    {
                        continue;
                    }

                    SpawnTile(tileData);
                }
            }

            Debug.Log($"타일 GameObject 생성 완료:{spawnedTiles.Count}개",this);
        }

        private void SpawnTile(CombatTileData tileData)
        {
            GameObject prefab =GetPrefab(tileData.Type);

            Transform parent =tileRoot != null? tileRoot: transform;

            GameObject instance =Instantiate(prefab,parent);

            instance.transform.localPosition =GridToLocalPosition(tileData.Position);

            instance.transform.localRotation =Quaternion.identity;

            CombatMapTileView tileView =instance.GetComponent<CombatMapTileView>();

            if (tileView == null)
            {
                Debug.LogError($"{prefab.name}에 CombatMapTileView가 없습니다.",prefab);

                DestroyTileObject(instance);
                return;
            }

            tileView.Initialize(tileData);

            spawnedTiles.Add(tileData.Position,tileView);
        }

        private GameObject GetPrefab(CombatTileType tileType)
        {
            return tileType switch
            {
                CombatTileType.Road =>roadTilePrefab,

                CombatTileType.Grass =>grassTilePrefab,

                CombatTileType.Water =>waterTilePrefab,

                _ =>null
            };
        }

        private Vector3 GridToLocalPosition(Vector2Int position)
        {
            return new Vector3((position.x + 0.5f) *tileSize,tileHeight,
                (position.y + 0.5f) *tileSize);
        }

        private bool ValidatePrefabs()
        {
            if (roadTilePrefab == null ||grassTilePrefab == null ||waterTilePrefab == null)
            {
                Debug.LogError("Road, Grass, Water 프리팹을 " +"모두 지정해야 합니다.",this);

                return false;
            }

            return true;
        }

        [ContextMenu("Clear Map Tiles")]
        public void ClearTiles()
        {
            Transform parent =tileRoot != null? tileRoot: transform;

            for (int i = parent.childCount - 1;i >= 0;i--)
            {
                DestroyTileObject(
                    parent.GetChild(i).gameObject);
            }

            spawnedTiles.Clear();
        }

        private void DestroyTileObject(GameObject target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}