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
        private CombatMapRevealController revealController;

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

        private readonly Dictionary<
            Vector2Int,
            CombatMapTileView> spawnedTiles =
            new Dictionary<Vector2Int, CombatMapTileView>();

        public int SpawnedTileCount =>
            spawnedTiles.Count;

        private void OnEnable()
        {
            SubscribeRevealEvent();
        }

        private void OnDisable()
        {
            UnsubscribeRevealEvent();
        }

        private void SubscribeRevealEvent()
        {
            if (revealController == null)
            {
                return;
            }

            // 중복 등록 방지
            revealController.RevealChanged -=
                RefreshTileVisibility;

            revealController.RevealChanged +=
                RefreshTileVisibility;
        }

        private void UnsubscribeRevealEvent()
        {
            if (revealController == null)
            {
                return;
            }

            revealController.RevealChanged -=
                RefreshTileVisibility;
        }

        // 생성된 타일을 좌표로 검색
        public bool TryGetTileView(
            Vector2Int position,
            out CombatMapTileView tileView)
        {
            return spawnedTiles.TryGetValue(
                position,
                out tileView);
        }

        [ContextMenu("Spawn Map Tiles")]
        public void SpawnTiles()
        {
            if (mapGenerator == null ||
                mapGenerator.CurrentMap == null)
            {
                Debug.LogError(
                    "먼저 전투맵 데이터를 생성해야 합니다.",
                    this);

                return;
            }

            if (!ValidatePrefabs())
            {
                return;
            }

            SubscribeRevealEvent();

            ClearTiles();

            CombatMapData map =
                mapGenerator.CurrentMap;

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position =
                        new Vector2Int(x, y);

                    CombatTileData tileData =
                        map.GetTile(position);

                    if (tileData.Type ==
                        CombatTileType.Empty)
                    {
                        continue;
                    }

                    SpawnTile(tileData);
                }
            }

            // 공개 데이터가 이미 있다면 즉시 적용
            if (revealController != null &&
                revealController.RevealData != null)
            {
                RefreshTileVisibility();
            }

            Debug.Log(
                $"타일 GameObject 생성 완료: " +
                $"{spawnedTiles.Count}개",
                this);
        }

        private void SpawnTile(
            CombatTileData tileData)
        {
            GameObject prefab =
                GetPrefab(tileData.Type);

            if (prefab == null)
            {
                Debug.LogError(
                    $"{tileData.Type}에 사용할 " +
                    "프리팹이 없습니다.",
                    this);

                return;
            }

            Transform parent =
                tileRoot != null
                    ? tileRoot
                    : transform;

            GameObject instance =
                Instantiate(
                    prefab,
                    parent);

            instance.transform.localPosition =
                GridToLocalPosition(
                    tileData.Position);

            instance.transform.localRotation =
                Quaternion.identity;

            CombatMapTileView tileView =
                instance.GetComponent<
                    CombatMapTileView>();

            if (tileView == null)
            {
                Debug.LogError(
                    $"{prefab.name}에 " +
                    "CombatMapTileView가 없습니다.",
                    prefab);

                DestroyTileObject(instance);
                return;
            }

            tileView.Initialize(tileData);

            spawnedTiles.Add(
                tileData.Position,
                tileView);
        }

        private GameObject GetPrefab(
            CombatTileType tileType)
        {
            return tileType switch
            {
                CombatTileType.Road =>
                    roadTilePrefab,

                CombatTileType.Grass =>
                    grassTilePrefab,

                CombatTileType.Water =>
                    waterTilePrefab,

                _ =>
                    null
            };
        }

        private Vector3 GridToLocalPosition(
            Vector2Int position)
        {
            return new Vector3(
                (position.x + 0.5f) *
                tileSize,
                tileHeight,
                (position.y + 0.5f) *
                tileSize);
        }

        private bool ValidatePrefabs()
        {
            if (roadTilePrefab == null ||
                grassTilePrefab == null ||
                waterTilePrefab == null)
            {
                Debug.LogError(
                    "Road, Grass, Water 프리팹을 " +
                    "모두 지정해야 합니다.",
                    this);

                return false;
            }

            return true;
        }

        // 공개 데이터에 따라 타일 GameObject 활성화
        [ContextMenu("Refresh Tile Visibility")]
        public void RefreshTileVisibility()
        {
            if (revealController == null)
            {
                Debug.LogError(
                    "Reveal Controller가 지정되지 않았습니다.",
                    this);

                return;
            }

            if (revealController.RevealData == null)
            {
                Debug.LogError(
                    "공개 데이터가 초기화되지 않았습니다.",
                    this);

                return;
            }

            int visibleTileCount = 0;

            foreach (KeyValuePair<
                         Vector2Int,
                         CombatMapTileView> pair
                     in spawnedTiles)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                bool isRevealed =
                    revealController.RevealData
                        .IsRevealed(pair.Key);

                pair.Value.gameObject.SetActive(
                    isRevealed);

                if (isRevealed)
                {
                    visibleTileCount++;
                }
            }

            Debug.Log(
                $"타일 표시 갱신 완료: " +
                $"{visibleTileCount}/" +
                $"{spawnedTiles.Count}개 공개",
                this);
        }

        [ContextMenu("Clear Map Tiles")]
        public void ClearTiles()
        {
            Transform parent =
                tileRoot != null
                    ? tileRoot
                    : transform;

            for (int i = parent.childCount - 1;
                 i >= 0;
                 i--)
            {
                DestroyTileObject(
                    parent.GetChild(i).gameObject);
            }

            spawnedTiles.Clear();
        }

        private void DestroyTileObject(
            GameObject target)
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

        [ContextMenu("Validate Spawned Tiles")]
        public void ValidateSpawnedTiles()
        {
            if (mapGenerator == null ||
                mapGenerator.CurrentMap == null)
            {
                Debug.LogError(
                    "검사할 맵 데이터가 없습니다.",
                    this);

                return;
            }

            CombatMapData map =
                mapGenerator.CurrentMap;

            int mapTileCount = 0;
            int missingTileCount = 0;
            int wrongTypeCount = 0;
            int unnecessaryTileCount = 0;

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    Vector2Int position =
                        new Vector2Int(x, y);

                    CombatTileData tileData =
                        map.GetTile(position);

                    bool hasSpawnedObject =
                        spawnedTiles.TryGetValue(
                            position,
                            out CombatMapTileView tileView);

                    if (tileData.Type ==
                        CombatTileType.Empty)
                    {
                        if (hasSpawnedObject)
                        {
                            unnecessaryTileCount++;
                        }

                        continue;
                    }

                    mapTileCount++;

                    if (!hasSpawnedObject)
                    {
                        missingTileCount++;
                        continue;
                    }

                    if (tileView.TileType !=
                        tileData.Type)
                    {
                        wrongTypeCount++;
                    }
                }
            }

            bool countMatches =
                mapTileCount ==
                spawnedTiles.Count;

            bool isValid =
                countMatches &&
                missingTileCount == 0 &&
                wrongTypeCount == 0 &&
                unnecessaryTileCount == 0;

            if (!isValid)
            {
                Debug.LogError(
                    "타일 시각화 검증 실패\n" +
                    $"맵 데이터 타일: " +
                    $"{mapTileCount}개\n" +
                    $"생성된 오브젝트: " +
                    $"{spawnedTiles.Count}개\n" +
                    $"누락: {missingTileCount}개\n" +
                    $"타입 불일치: " +
                    $"{wrongTypeCount}개\n" +
                    $"불필요한 오브젝트: " +
                    $"{unnecessaryTileCount}개",
                    this);

                return;
            }

            Debug.Log(
                "타일 시각화 검증 완료\n" +
                $"맵 데이터 타일: " +
                $"{mapTileCount}개\n" +
                $"생성된 오브젝트: " +
                $"{spawnedTiles.Count}개",
                this);
        }
    }
}