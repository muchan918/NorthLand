using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;


namespace CombatSpace
{
    // 신규 전투맵의 경로·스폰 위치·웨이브 진행을
    // 기존 MonsterSpawn 시스템에 연결
    public sealed class CombatMapMonsterConnector :
        MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private CombatMapTileSpawner tileSpawner;

        [SerializeField]
        private CombatMapRevealController revealController;

        [SerializeField]
        private MonsterSpawn monsterSpawn;

        [SerializeField]
        private FixedEnemyRoute fixedEnemyRoute;

        private DayNightManager subscribedDayNightManager;

        [SerializeField]
        private float spawnDelaySeconds = 1f;


        [Header("Route Connection")]
        [SerializeField]
        [Min(0f)]
        private float duplicatePointDistance = 0.25f;

        private readonly List<Vector3> fixedWorldRoute = new List<Vector3>();

        private readonly List<Vector3> combinedWorldRoute = new List<Vector3>();

        private void OnEnable()
        {
            SubscribeRevealEvent();
            TrySubscribeDayNightEvent();
        }

        private void Start()
        {
            // 다른 오브젝트의 Awake 이후 다시 연결 시도
            TrySubscribeDayNightEvent();
        }

        private void OnDisable()
        {
            UnsubscribeRevealEvent();
            UnsubscribeDayNightEvent();
        }

        private void SubscribeRevealEvent()
        {
            if (revealController == null)
            {
                return;
            }

            // 중복 등록 방지
            revealController.RevealChanged -=RefreshMonsterMapData;

            revealController.RevealChanged +=RefreshMonsterMapData;
        }

        private void UnsubscribeRevealEvent()
        {
            if (revealController == null)
            {
                return;
            }

            revealController.RevealChanged -= RefreshMonsterMapData;
        }

        private void TrySubscribeDayNightEvent()
        {
            DayNightManager dayNightManager = DayNightManager.Instance;

            if (dayNightManager == null)
            {
                return;
            }

            if (subscribedDayNightManager ==
                dayNightManager)
            {
                return;
            }

            UnsubscribeDayNightEvent();

            subscribedDayNightManager = dayNightManager;

            subscribedDayNightManager.OnDayToNight += HandleDayToNight;
        }

        private void UnsubscribeDayNightEvent()
        {
            if (subscribedDayNightManager == null)
            {
                return;
            }

            subscribedDayNightManager.OnDayToNight -= HandleDayToNight;

            subscribedDayNightManager = null;
        }

        // 낮에서 밤으로 바뀌면 맵을 공개하고 해당 웨이브 시작
        private void HandleDayToNight()
        {
            HandleDayToNightAsync().Forget();
        }

        private async UniTaskVoid HandleDayToNightAsync()
        {
            if (!ValidateReferences())
            {
                return;
            }

            DayNightManager dayNightManager =DayNightManager.Instance;

            if (dayNightManager == null)
            {
                Debug.LogError("DayNightManager가 없습니다.",this);

                return;
            }

            int waveNumber = dayNightManager.CurrentWave;

            revealController.RevealForRound(waveNumber);

            RefreshMonsterMapData();

            float actualSpawnDelay =Mathf.Max(spawnDelaySeconds,tileSpawner.MaxRevealTime);

            await WaitForSpawnDelayAsync(actualSpawnDelay,this.GetCancellationTokenOnDestroy());

            monsterSpawn.StartRound(waveNumber);

            Debug.Log($"Wave {waveNumber} 몬스터 스폰 시작",this);
        }

        // 현재 공개된 월드 경로와 스폰 Pose 전달
        [ContextMenu("Refresh Monster Map Data")]
        public void RefreshMonsterMapData()
        {
            if (!ValidateReferences())
            {
                return;
            }

            tileSpawner.RefreshWorldEnemyRoute();

            if (tileSpawner.CurrentWorldEnemyRoute == null ||
                tileSpawner.CurrentWorldEnemyRoute.Count == 0)
            {
                Debug.LogWarning("몬스터에게 전달할 월드 경로가 없습니다.",this);

                return;
            }

            if (!tileSpawner.TryGetCurrentSpawnPose(out Vector3 spawnPosition,out Quaternion spawnRotation))
            {
                Debug.LogWarning(
                    "몬스터 스폰 Pose를 가져오지 못했습니다.",
                    this);

                return;
            }

            if (!TryBuildCombinedRoute())
            {
                Debug.LogWarning("몬스터 전체 경로 결합에 실패했습니다.",this);

                return;
            }

            monsterSpawn.SetRoute(combinedWorldRoute);

            monsterSpawn.SetSpawnPoint(spawnPosition,spawnRotation);

            Debug.Log($"몬스터 맵 데이터 갱신 완료\n고정 경로: {fixedWorldRoute.Count}개\n" +
        $"자동 경로: {tileSpawner.CurrentWorldEnemyRoute.Count}개\n전체 경로: {combinedWorldRoute.Count}개\n" +
        $"스폰 위치: {spawnPosition}",this);
        }

        private bool ValidateReferences()
        {
            if (tileSpawner == null)
            {
                Debug.LogError("Tile Spawner가 지정되지 않았습니다.",this);

                return false;
            }

            if (revealController == null)
            {
                Debug.LogError("Reveal Controller가 지정되지 않았습니다.",this);

                return false;
            }

            if (monsterSpawn == null)
            {
                Debug.LogError("Monster Spawn이 지정되지 않았습니다.",this);

                return false;
            }

            if (fixedEnemyRoute == null)
            {
                Debug.LogError("Fixed Enemy Route가 지정되지 않았습니다.",this);

                return false;
            }

            return true;
        }

        private async UniTask WaitForSpawnDelayAsync(float duration,CancellationToken cancellationToken)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                GameSpeedController speedController = GameSpeedController.Instance;

                bool isPaused = speedController != null &&speedController.IsPaused;

                if (!isPaused)
                {
                    elapsed += Time.unscaledDeltaTime;
                }

                await UniTask.Yield(PlayerLoopTiming.Update,cancellationToken);
            }
        }
        private bool TryBuildCombinedRoute()
        {
            combinedWorldRoute.Clear();

            if (!fixedEnemyRoute.TryGetWorldPoints(fixedWorldRoute))
            {
                return false;
            }

            IReadOnlyList<Vector3> generatedRoute = tileSpawner.CurrentWorldEnemyRoute;

            if (generatedRoute == null || generatedRoute.Count == 0)
            {
                Debug.LogWarning("결합할 자동 생성 경로가 없습니다.",this);

                return false;
            }

            combinedWorldRoute.AddRange(fixedWorldRoute);

            Vector3 fixedEnd = fixedWorldRoute[fixedWorldRoute.Count - 1];

            Vector3 generatedStart = generatedRoute[0];

            float connectionDistance = Vector3.Distance(fixedEnd, generatedStart);

            if (connectionDistance > tileSpawner.TileSize)
            {
                Debug.LogError($"고정 경로와 자동 경로 사이가 너무 멉니다. " +
                    $"거리: {connectionDistance:F2}, 허용 거리: {tileSpawner.TileSize:F2}",this);

                combinedWorldRoute.Clear();
                return false;
            }

            int generatedStartIndex = connectionDistance <= duplicatePointDistance? 1: 0;

            for (int i = generatedStartIndex;i < generatedRoute.Count;i++)
            {
                combinedWorldRoute.Add(generatedRoute[i]);
            }

            return combinedWorldRoute.Count >= 2;
        }
    }
}