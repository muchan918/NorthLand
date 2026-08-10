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

        [SerializeField]
        [Min(0f)]
        private float maxConnectionDistance = 1f;

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

            revealController.RevealChanged -= HandleRevealChanged;

            revealController.RevealChanged += HandleRevealChanged;
        }

        private void HandleRevealChanged()
        {
            RefreshMonsterMapData();
        }

        private void UnsubscribeRevealEvent()
        {
            if (revealController == null)
            {
                return;
            }

            revealController.RevealChanged -= HandleRevealChanged;
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

            DayNightManager dayNightManager = DayNightManager.Instance;

            if (dayNightManager == null)
            {
                Debug.LogError("DayNightManager가 없습니다.", this);
                return;
            }

            int waveNumber = dayNightManager.CurrentWave;

            revealController.RevealForRound(waveNumber);

            if (!RefreshMonsterMapData())
            {
                Debug.LogError("[CombatMapMonsterConnector] 경로 생성 실패로 웨이브를 시작하지 않습니다.", this);

                return;
            }

            float actualSpawnDelay = Mathf.Max(spawnDelaySeconds, tileSpawner.MaxRevealTime);

            await WaitForSpawnDelayAsync(actualSpawnDelay, this.GetCancellationTokenOnDestroy());

            monsterSpawn.StartRound(waveNumber);

            Debug.Log($"Wave {waveNumber} 몬스터 스폰 시작", this);
        }

        // 현재 공개된 월드 경로와 스폰 Pose 전달
        [ContextMenu("Refresh Monster Map Data")]
        private bool RefreshMonsterMapData()
        {
            if (!ValidateReferences())
            {
                return false;
            }

            tileSpawner.RefreshWorldEnemyRoute();

            if (!tileSpawner.TryGetCurrentSpawnPose(
                    out Vector3 spawnPosition,
                    out Quaternion spawnRotation))
            {
                Debug.LogError("몬스터 스폰 위치를 가져오지 못했습니다.", this);
                return false;
            }

            if (!TryBuildCombinedRoute())
            {
                Debug.LogError("몬스터 전체 경로 결합에 실패했습니다.", this);
                return false;
            }

            monsterSpawn.SetRoute(combinedWorldRoute);
            monsterSpawn.SetSpawnPoint(spawnPosition, spawnRotation);

            return true;
        }
        private bool ValidateReferences()
        {
            if (tileSpawner == null)
            {
                Debug.LogError("Tile Spawner가 지정되지 않았습니다.", this);

                return false;
            }

            if (revealController == null)
            {
                Debug.LogError("Reveal Controller가 지정되지 않았습니다.", this);

                return false;
            }

            if (monsterSpawn == null)
            {
                Debug.LogError("Monster Spawn이 지정되지 않았습니다.", this);

                return false;
            }

            if (fixedEnemyRoute == null)
            {
                Debug.LogError("Fixed Enemy Route가 지정되지 않았습니다.", this);

                return false;
            }

            return true;
        }

        private async UniTask WaitForSpawnDelayAsync(float duration, CancellationToken cancellationToken)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                GameSpeedController speedController = GameSpeedController.Instance;

                bool isPaused = speedController != null && speedController.IsPaused;

                if (!isPaused)
                {
                    elapsed += Time.unscaledDeltaTime;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
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
                Debug.LogWarning("결합할 자동 생성 경로가 없습니다.", this);

                return false;
            }

            Vector3 generatedStart = generatedRoute[0];
            Vector3 fixedEnd = fixedWorldRoute[fixedWorldRoute.Count - 1];

            // 고정 경로 전체의 높이를 자동 경로 시작 높이에 맞춘다.
            float heightOffset = generatedStart.y - fixedEnd.y;

            for (int i = 0; i < fixedWorldRoute.Count; i++)
            {
                fixedWorldRoute[i] += Vector3.up * heightOffset;
            }

            // 높이 보정 후 끝점을 다시 가져온다.
            fixedEnd = fixedWorldRoute[fixedWorldRoute.Count - 1];

            // Y를 제외한 XZ 평면상의 거리만 계산한다.
            Vector3 connectionDelta = generatedStart - fixedEnd;
            connectionDelta.y = 0f;

            float connectionDistance = connectionDelta.magnitude;

            if (connectionDistance > maxConnectionDistance)
            {
                Debug.LogError($"고정 경로와 자동 경로 사이가 너무 멉니다. " +
                    $"XZ 거리: {connectionDistance:F2},허용 거리: {maxConnectionDistance:F2}", this);

                return false;
            }
            // 접합 지점의 XZ 위치가 같으면 자동 경로 첫 지점을 생략한다.
            int generatedStartIndex = connectionDistance <= duplicatePointDistance ? 1 : 0;

            // 0라운드는 실제 웨이브가 아니라 초기 공개 단계다.
            // 이때는 자동 경로가 중복 접합점 하나뿐일 수 있으므로
            // 고정 경로만으로 성문과 스폰 데이터를 초기화하도록 허용한다.
            bool isInitialReveal = revealController.CurrentRound == 0;

            if (generatedStartIndex >= generatedRoute.Count &&
                !isInitialReveal)
            {
                Debug.LogError("[CombatMapMonsterConnector] 접합 지점을 제외하면 " +
                    "사용할 수 있는 자동 생성 경로가 없습니다.", this);

                return false;
            }

            // 검증이 끝난 뒤 결합 경로를 구성한다.
            combinedWorldRoute.AddRange(fixedWorldRoute);

            for (int i = generatedStartIndex;i < generatedRoute.Count;i++)
            {
                combinedWorldRoute.Add(generatedRoute[i]);
            }

            return combinedWorldRoute.Count >= 2;
        }
    }
}
