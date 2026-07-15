using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class MonsterSpawn : MonoBehaviour
{
    [Header("Common References")]
    [SerializeField] private Transform fallbackSpawnPoint;
    [SerializeField] private Transform monsterParent;

    [Header("ScriptableObject Data")]
    [SerializeField] private MonsterSpawnWaveProvider waveProvider;
    [SerializeField] private bool playOnStart;
    [SerializeField] private int startRound = 1;

    [Header("Gate (성문)")]
    [Tooltip("몬스터가 도달해 제거되는 경로 끝점에 생성할 성문 프리팹. 비워두면 생성하지 않는다.")]
    [SerializeField] private GameObject gatePrefab;

    private GameObject gateInstance;
    private bool hasGeneratedSpawnPoint;
    private Vector3 generatedSpawnPosition;
    private Quaternion generatedSpawnRotation = Quaternion.identity;
    private readonly List<Vector3> route = new List<Vector3>();
    private readonly List<Vector3> spawnRoute = new List<Vector3>();
    private CancellationTokenSource spawnCancellationTokenSource;

    private void Awake()
    {
        if (waveProvider == null)
        {
            waveProvider = GetComponent<MonsterSpawnWaveProvider>();
        }
    }

    private void Start()
    {
        if (playOnStart)
        {
            StartRound(startRound);
        }
    }

    private void OnDisable()
    {
        CancelSpawnTasks();
    }

    private void OnDestroy()
    {
        CancelSpawnTasks();
    }

    public void SetSpawnPoint(Vector3 position, Quaternion rotation)
    {
        generatedSpawnPosition = position;
        generatedSpawnRotation = rotation;
        hasGeneratedSpawnPoint = true;
    }

    public void SetRoute(IReadOnlyList<Vector3> routePoints)
    {
        route.Clear();

        if (routePoints == null)
        {
            return;
        }

        route.AddRange(routePoints);

        UpdateGate();
    }

    // 성문(gatePrefab)을 경로 끝점 — 몬스터가 도달해 제거되는 지점 — 에 배치한다.
    // 몬스터는 GetSpawnRoute()(route를 뒤집은 경로)를 따라가다 마지막 지점에서 MonsterMove에 의해
    // 제거되며, 그 지점은 route[0]에 해당한다. 경로가 갱신되면(스테이지 재생성 등) 위치를 옮긴다.
    // monsterParent에 붙이지 않는다 — 웨이브 클리어는 monsterParent.childCount로 판정하므로(WL-037).
    private void UpdateGate()
    {
        if (gatePrefab == null || route.Count == 0)
        {
            return;
        }

        Vector3 endPoint = route[0];

        if (gateInstance == null)
        {
            gateInstance = Instantiate(gatePrefab, endPoint, Quaternion.identity);
        }
        else
        {
            gateInstance.transform.position = endPoint;
        }
    }

    public void StartRound(int round)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (DayNightManager.Instance != null &&
            DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day)
        {
            Debug.LogWarning($"[몬스터 스포너] 낮에는 몬스터를 소환하지 않습니다. 라운드 {round} 소환을 건너뜁니다.");
            return;
        }

        // 스폰할 웨이브가 없으면(스포너 미설정 또는 이 라운드 데이터 없음) 그 밤이 EndNight에
        // 닿지 못해 소프트락된다 → "즉시 클리어"로 간주해 밤을 끝낸다(WL-037).
        if (waveProvider == null)
        {
            Debug.LogWarning("[몬스터 스포너] waveProvider 미설정 — 스폰 없이 즉시 웨이브 클리어(밤 종료) 처리합니다.");
            EndNightIfNight();
            return;
        }

        if (!waveProvider.TryGetWave(round, out IReadOnlyList<MonsterSpawnEntry> entries))
        {
            Debug.LogWarning($"[몬스터 스포너] 라운드 {round} 웨이브 데이터 없음 — 스폰 없이 즉시 웨이브 클리어(밤 종료) 처리합니다.");
            EndNightIfNight();
            return;
        }

        CancellationToken cancellationToken = RestartSpawnTasks();
        SpawnRoundAsync(entries, cancellationToken).Forget();
    }

    private async UniTaskVoid SpawnRoundAsync(IReadOnlyList<MonsterSpawnEntry> entries, CancellationToken cancellationToken)
    {
        try
        {
            List<UniTask> groupTasks = new List<UniTask>();
            float elapsedDelay = 0f;

            foreach (MonsterSpawnEntry entry in entries.OrderBy(e => e.StartDelay))
            {
                cancellationToken.ThrowIfCancellationRequested();

                float waitTime = Mathf.Max(0f, entry.StartDelay - elapsedDelay);
                if (waitTime > 0f)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(waitTime),
                        cancellationToken: cancellationToken
                    );

                    elapsedDelay = entry.StartDelay;
                }

                groupTasks.Add(SpawnGroupAsync(entry, cancellationToken));
            }

            await UniTask.WhenAll(groupTasks);

            // 스폰이 모두 끝난 뒤, 살아있는 몬스터(monsterParent의 자식)가 0이 되면 웨이브 클리어.
            // 몬스터는 처치(Enemy.Die) 또는 본진 도달(MonsterMove) 시 Destroy되어 자식에서 빠진다.
            if (monsterParent == null)
            {
                Debug.LogWarning("[몬스터 스포너] monsterParent 미할당 — 웨이브 클리어 자동 감지를 건너뜁니다.");
                return;
            }

            await UniTask.WaitUntil(() => monsterParent.childCount == 0, cancellationToken: cancellationToken);
            EndNightIfNight();
        }
        catch (OperationCanceledException)
        {
        }
    }

    // 웨이브 클리어 시 밤을 종료한다. 밤이 아닐 때(수동으로 이미 낮 전환 등) 호출은 무시한다.
    // DayNightManager는 StartRound에서 이미 참조하는 의존이라 새 결합을 늘리지 않는다.
    private void EndNightIfNight()
    {
        DayNightManager dayNight = DayNightManager.Instance;
        if (dayNight == null || dayNight.CurrentPhase != DayNightManager.Phase.Night)
        {
            return;
        }

        dayNight.EndNight();
    }

    private async UniTask SpawnGroupAsync(MonsterSpawnEntry entry, CancellationToken cancellationToken)
    {
        int spawnCount = Mathf.Max(0, entry.Count);

        for (int i = 0; i < spawnCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpawnPrefab(entry.MonsterPrefab);

            if (i < spawnCount - 1 && entry.SpawnInterval > 0f)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(entry.SpawnInterval),
                    cancellationToken: cancellationToken
                );
            }
        }
    }

    private CancellationToken RestartSpawnTasks()
    {
        CancelSpawnTasks();

        spawnCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            this.GetCancellationTokenOnDestroy()
        );

        return spawnCancellationTokenSource.Token;
    }

    private bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        if (hasGeneratedSpawnPoint)
        {
            position = generatedSpawnPosition;
            rotation = generatedSpawnRotation;
            return true;
        }

        if (fallbackSpawnPoint != null)
        {
            position = fallbackSpawnPoint.position;
            rotation = fallbackSpawnPoint.rotation;
            return true;
        }

        position = default;
        rotation = Quaternion.identity;
        return false;
    }

    private void SpawnPrefab(GameObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        if (!TryGetSpawnPose(out Vector3 position, out Quaternion rotation))
        {
            return;
        }

        GameObject monster = Instantiate(prefab, position, rotation, monsterParent);
        MonsterMove monsterMove = monster.GetComponent<MonsterMove>();

        if (monsterMove == null)
        {
            monsterMove = monster.GetComponentInChildren<MonsterMove>();
        }

        if (monsterMove != null)
        {
            monsterMove.SetRoute(GetSpawnRoute());
        }
        else
        {
            // MonsterMove가 없으면 이동·본진 도달 디스폰이 없어 웨이브 클리어(childCount 0)에 닿지 못한다(WL-037).
            Debug.LogWarning($"[몬스터 스포너] '{monster.name}'에 MonsterMove가 없어 이동/디스폰하지 않습니다 — 웨이브가 끝나지 않을 수 있습니다.", monster);
        }
    }

    private List<Vector3> GetSpawnRoute()
    {
        spawnRoute.Clear();

        for (int i = route.Count - 1; i >= 0; i--)
        {
            spawnRoute.Add(route[i]);
        }

        return spawnRoute;
    }

    private void CancelSpawnTasks()
    {
        if (spawnCancellationTokenSource == null)
        {
            return;
        }

        spawnCancellationTokenSource.Cancel();
        spawnCancellationTokenSource.Dispose();
        spawnCancellationTokenSource = null;
    }
}
