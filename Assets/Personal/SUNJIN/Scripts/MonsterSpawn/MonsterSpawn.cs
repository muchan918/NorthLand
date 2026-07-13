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

        if (!waveProvider.TryGetWave(round, out IReadOnlyList<MonsterSpawnEntry> entries))
        {
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

        Instantiate(prefab, position, rotation, monsterParent);
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
