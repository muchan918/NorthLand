using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
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

        [Header("Reveal Animation")]
        [SerializeField]
        [Min(0f)]
        private float revealYOffset = 20f;

        [SerializeField]
        [Min(0.01f)]
        private float revealDuration = 0.5f;

        [SerializeField]
        private AnimationCurve revealCurve =AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);


        [Tooltip("Road 타일 피벗에서 몬스터 발 위치까지의 월드 Y 오프셋. " +"Road 프리팹의 피벗·높이 변경 시 함께 조정해야 합니다.")]
        [Header("Monster Route")]
        [SerializeField]
        private float monsterWaypointYOffset = 6f;


        private readonly Dictionary<Vector2Int,CombatMapTileView> spawnedTiles = new Dictionary<Vector2Int, CombatMapTileView>();

        private readonly HashSet<BuffTileDefinition>warnedMissingBuffPrefabs =new HashSet<BuffTileDefinition>();

        // 현재 공개된 Road의 월드 좌표 경로
        private readonly List<Vector3> currentWorldEnemyRoute =new List<Vector3>();

        public IReadOnlyList<Vector3> CurrentWorldEnemyRoute =>currentWorldEnemyRoute;

        public float TileSize =>mapGenerator != null &&mapGenerator.Settings != null? mapGenerator.Settings.TileSize: 1f;

        public int SpawnedTileCount =>spawnedTiles.Count;

        private void OnEnable()
        {
            RebuildSpawnedTileCache();
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
            if (spawnedTiles.Count == 0)
            {
                RebuildSpawnedTileCache();
            }

            return spawnedTiles.TryGetValue(
                position,
                out tileView);
        }

        [ContextMenu("Spawn Map Tiles")]
        public void SpawnTiles()
        {
            if (mapGenerator == null ||mapGenerator.CurrentMap == null)
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
            warnedMissingBuffPrefabs.Clear();

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

            // 공개 데이터가 이미 있다면 즉시 적용
            if (revealController != null &&revealController.RevealData != null)
            {
                RefreshTileVisibility();
            }

            Debug.Log(
                $"타일 GameObject 생성 완료: " +
                $"{spawnedTiles.Count}개",
                this);
        }

        private void SpawnTile(CombatTileData tileData)
        {
            GameObject prefab =GetPrefab(tileData);

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

        private GameObject GetPrefab(CombatTileData tileData)
        {
            if (tileData == null)
            {
                return null;
            }

            return tileData.Type switch
            {
                CombatTileType.Road => roadTilePrefab,
                CombatTileType.Grass => GetGrassPrefab(tileData),
                CombatTileType.Water => waterTilePrefab,
                _ => null
            };
        }

        private GameObject GetGrassPrefab(CombatTileData tileData)
        {
            BuffTileDefinition definition =tileData.BuffDefinition;

            // Definition 자체가 없으면 기본 잔디를 사용한다.
            if (definition == null)
            {
                return grassTilePrefab;
            }

            // Definition은 있지만 프리팹이 없으면 종류별로 한 번만 경고한다.
            if (definition.Prefab == null)
            {
                if (warnedMissingBuffPrefabs.Add(definition))
                {
                    Debug.LogWarning($"버프 타일 '{definition.Id}'의 프리팹이 할당되지 않아 일반 잔디로 대체합니다.",this);
                }

                return grassTilePrefab;
            }

            return definition.Prefab;
        }

        private Vector3 GridToLocalPosition(Vector2Int position)
        {
            CombatMapGenerationSettings settings =mapGenerator.Settings;

            return new Vector3((position.x + 0.5f) *settings.TileSize,

                settings.TileHeight,

                (position.y + 0.5f) *settings.TileSize);
        }


        // 그리드 좌표를 실제 월드 좌표로 변환
        public Vector3 GridToWorldPosition(Vector2Int gridPosition)
        {
            Transform coordinateRoot =tileRoot != null? tileRoot: transform;

            Vector3 localPosition =GridToLocalPosition(gridPosition);

            return coordinateRoot.TransformPoint(localPosition);
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

        // 자식 타일을 검색하여 딕셔너리 복구
        [ContextMenu("Rebuild Spawned Tile Cache")]
        public void RebuildSpawnedTileCache()
        {
            spawnedTiles.Clear();

            Transform parent =
                tileRoot != null
                    ? tileRoot
                    : transform;

            CombatMapTileView[] tileViews =
                parent.GetComponentsInChildren<
                    CombatMapTileView>(true);

            int duplicateCount = 0;

            foreach (CombatMapTileView tileView
                     in tileViews)
            {
                if (tileView == null)
                {
                    continue;
                }

                Vector2Int position =
                    tileView.GridPosition;

                if (spawnedTiles.ContainsKey(position))
                {
                    duplicateCount++;

                    Debug.LogWarning(
                        $"중복 타일 좌표 발견: " +
                        $"{position}",
                        tileView);

                    continue;
                }

                spawnedTiles.Add(
                    position,
                    tileView);
            }

            Debug.Log(
                "타일 캐시 복구 완료\n" +
                $"복구된 타일: " +
                $"{spawnedTiles.Count}개\n" +
                $"중복 좌표: " +
                $"{duplicateCount}개",
                this);
        }

        [ContextMenu("Refresh Tile Visibility")]
        public void RefreshTileVisibility()
        {
            if (revealController == null)
            {
                Debug.LogError(
                    "Reveal Controller가 지정되지 않았습니다.",
                    this
                );

                return;
            }

            if (revealController.RevealData == null)
            {
                Debug.LogError(
                    "공개 데이터가 초기화되지 않았습니다.",
                    this
                );

                return;
            }

            if (spawnedTiles.Count == 0)
            {
                RebuildSpawnedTileCache();
            }

            int visibleTileCount = 0;

            foreach (KeyValuePair<Vector2Int, CombatMapTileView> pair
                     in spawnedTiles)
            {
                CombatMapTileView tileView = pair.Value;

                if (tileView == null)
                {
                    continue;
                }

                GameObject tileObject = tileView.gameObject;
                bool wasVisible = tileObject.activeSelf;

                bool isRevealed =
                    revealController.RevealData.IsRevealed(pair.Key);

                tileObject.SetActive(isRevealed);

                if (!isRevealed)
                {
                    continue;
                }

                visibleTileCount++;

                // 이미 공개되어 있던 타일은 움직이지 않고
                // 이번에 새로 공개된 타일만 상승시킨다.
                if (!wasVisible)
                {
                    PlayTileRevealAsync(
                        tileView.transform,
                        tileView.GetCancellationTokenOnDestroy()
                    ).Forget();
                }
            }

            // 공개 범위가 바뀌었으므로 몬스터 경로 캐시도 갱신
            RebuildCurrentWorldEnemyRoute();

            Debug.Log(
                $"타일 표시 갱신 완료: " +
                $"{visibleTileCount}/{spawnedTiles.Count}개 공개\n" +
                $"몬스터 월드 경로: " +
                $"{currentWorldEnemyRoute.Count}개",
                this
            );
        }

        private async UniTask PlayTileRevealAsync(
        Transform tileTransform,
        CancellationToken cancellationToken)
        {
            Vector3 targetPosition = tileTransform.localPosition;
            Vector3 startPosition =
                targetPosition + Vector3.down * revealYOffset;

            tileTransform.localPosition = startPosition;

            try
            {
                float elapsed = 0f;

                while (elapsed < revealDuration)
                {
                    elapsed += Time.deltaTime;

                    float normalizedTime =
                        Mathf.Clamp01(elapsed / revealDuration);

                    float curvedTime =
                        revealCurve.Evaluate(normalizedTime);

                    tileTransform.localPosition =
                        Vector3.LerpUnclamped(
                            startPosition,
                            targetPosition,
                            curvedTime
                        );

                    await UniTask.Yield(
                        PlayerLoopTiming.Update,
                        cancellationToken
                    );
                }

                tileTransform.localPosition = targetPosition;
            }
            catch (OperationCanceledException)
            {
                // 타일이 제거되거나 씬이 종료된 정상적인 취소
            }
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
            currentWorldEnemyRoute.Clear();
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

            if (spawnedTiles.Count == 0)
            {
                RebuildSpawnedTileCache();
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
                    $"타일 시각화 검증 실패\n맵 데이터 타일: {mapTileCount}개\n 생성된 오브젝트: {spawnedTiles.Count} 개\n 누락: {missingTileCount}개\n" +
                    $"타입 불일치: {wrongTypeCount}개\n불필요한 오브젝트: {unnecessaryTileCount}개", this);

                return;
            }

            Debug.Log(
                $"타일 시각화 검증 완료\n 맵 데이터 타일:{mapTileCount}개\n생성된 오브젝트:{spawnedTiles.Count}개",this);
        }

        // 현재 공개 범위에 맞춰 몬스터용 월드 경로 갱신
        public void RefreshWorldEnemyRoute()
        {
            RebuildCurrentWorldEnemyRoute();
        }

        // 시작점부터 현재 스폰 위치까지 월드 경로 생성
        private void RebuildCurrentWorldEnemyRoute()
        {
            currentWorldEnemyRoute.Clear();
            if (mapGenerator == null ||
                mapGenerator.CurrentMap == null ||
                mapGenerator.CurrentMap.EnemyRoute.Count == 0 ||
                revealController == null ||
                !revealController.HasSpawnPosition)
            {
                return;
            }

            CombatMapData map =
                mapGenerator.CurrentMap;

            int finalRouteIndex =
                Mathf.Clamp(revealController.CurrentSpawnRouteIndex,0,map.EnemyRoute.Count - 1);

            for (int routeIndex = 0;
                 routeIndex <= finalRouteIndex;
                 routeIndex++)
            {
                Vector2Int gridPosition =
                    map.EnemyRoute[routeIndex];

                Vector3 worldPosition = GridToWorldPosition(gridPosition);

                worldPosition.y += monsterWaypointYOffset;

                currentWorldEnemyRoute.Add(worldPosition);
            }
        }
        // 현재 공개된 마지막 Road의 월드 스폰 Pose 제공
        public bool TryGetCurrentSpawnPose(
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;

            if (mapGenerator == null ||
      mapGenerator.CurrentMap == null ||
      mapGenerator.CurrentMap.EnemyRoute.Count == 0 ||
      revealController == null ||
      !revealController.HasSpawnPosition)
            {
                return false;
            }

            CombatMapData map =
                mapGenerator.CurrentMap;

            int spawnRouteIndex =
                revealController
                    .CurrentSpawnRouteIndex;

            if (spawnRouteIndex < 0 ||
                spawnRouteIndex >=
                map.EnemyRoute.Count)
            {
                return false;
            }

            position =GridToWorldPosition(map.EnemyRoute[spawnRouteIndex]);

            position.y += monsterWaypointYOffset;

            Transform coordinateRoot =
                tileRoot != null
                    ? tileRoot
                    : transform;

            // 이전 Road 방향, 즉 성문 방향을 바라봄
            if (spawnRouteIndex > 0)
            {
                Vector3 nextPosition =GridToWorldPosition(map.EnemyRoute[spawnRouteIndex - 1]);
                nextPosition.y += monsterWaypointYOffset;

                Vector3 direction = nextPosition - position;
                direction.y = 0f;

                if(direction.sqrMagnitude > 0.0001f)
                {
                    rotation =
                        Quaternion.LookRotation(
                            direction.normalized,
                            coordinateRoot.up);

                    return true;
                }
            }

            rotation =
                coordinateRoot.rotation;

            return true;
        }
    }
}