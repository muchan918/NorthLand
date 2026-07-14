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

    public void SetRoute(List<Vector3> routePoints)
    {
        route.Clear();

        if (routePoints == null)
        {
            return;
        }

        route.AddRange(routePoints);
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
            return;
        }

        if (waveProvider == null)
        {
            return;
        }

        if (!waveProvider.TryGetWave(round, out List<MonsterSpawnEntry> entries))
        {
            return;
        }

        CancellationToken cancellationToken = RestartSpawnTasks();
        SpawnRoundAsync(entries, cancellationToken).Forget();
    }

    private async UniTaskVoid SpawnRoundAsync(List<MonsterSpawnEntry> entries, CancellationToken cancellationToken)
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
        }
        catch (OperationCanceledException)
        {
        }
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
