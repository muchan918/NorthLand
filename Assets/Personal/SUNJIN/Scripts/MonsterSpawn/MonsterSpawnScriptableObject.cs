using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class MonsterSpawnScriptableObject : MonsterSpawn
{
    [Header("ScriptableObject Data")]
    [SerializeField] private List<MonsterSpawnWave> spawnWaves = new List<MonsterSpawnWave>();
    [SerializeField] private bool playOnStart;
    [SerializeField] private int startRound = 1;

    private void Start()
    {
        if (playOnStart)
        {
            StartRound(startRound);
        }
    }

    public override void StartRound(int round)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        CancellationToken cancellationToken = RestartSpawnTasks();
        SpawnRoundAsync(round, cancellationToken).Forget();
    }

    private async UniTaskVoid SpawnRoundAsync(int round, CancellationToken cancellationToken)
    {
        try
        {
            MonsterSpawnWave wave = spawnWaves.Find(w => w != null && w.Round == round);

            if (wave == null)
            {
                Debug.LogWarning($"MonsterSpawnScriptableObject: round {round} wave data is missing.", this);
                return;
            }

            List<UniTask> groupTasks = new List<UniTask>();
            float elapsedDelay = 0f;

            foreach (MonsterSpawnEntry entry in wave.Entries.OrderBy(e => e.StartDelay))
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
}
