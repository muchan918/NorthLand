using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class MonsterSpawnCsv : MonsterSpawn
{
    [Header("CSV Data")]
    [SerializeField] private string spawnTableName = "MonsterSpawnTable";
    [SerializeField] private List<MonsterPrefabEntry> monsterPrefabs = new List<MonsterPrefabEntry>();
    [SerializeField] private bool playOnStart;
    [SerializeField] private int startRound = 1;

    private readonly MonsterSpawnTable spawnTable = new MonsterSpawnTable();
    private Dictionary<string, GameObject> prefabById = new Dictionary<string, GameObject>();

    private void Awake()
    {
        LoadData();
    }

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

    private void LoadData()
    {
        spawnTable.Load(spawnTableName);

        prefabById = monsterPrefabs
            .Where(entry => !string.IsNullOrWhiteSpace(entry.MonsterId) && entry.MonsterPrefab != null)
            .GroupBy(entry => entry.MonsterId)
            .ToDictionary(group => group.Key, group => group.First().MonsterPrefab);
    }

    private async UniTaskVoid SpawnRoundAsync(int round, CancellationToken cancellationToken)
    {
        try
        {
            List<MonsterSpawnData> roundSpawnList = spawnTable.GetRound(round);

            if (roundSpawnList.Count == 0)
            {
                Debug.LogWarning($"MonsterSpawnCsv: round {round} spawn data is missing.", this);
                return;
            }

            List<UniTask> groupTasks = new List<UniTask>();
            float elapsedDelay = 0f;

            foreach (MonsterSpawnData spawnData in roundSpawnList.OrderBy(data => data.StartDelay))
            {
                cancellationToken.ThrowIfCancellationRequested();

                float waitTime = Mathf.Max(0f, spawnData.StartDelay - elapsedDelay);
                if (waitTime > 0f)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(waitTime),
                        cancellationToken: cancellationToken
                    );

                    elapsedDelay = spawnData.StartDelay;
                }

                groupTasks.Add(SpawnGroupAsync(spawnData, cancellationToken));
            }

            await UniTask.WhenAll(groupTasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async UniTask SpawnGroupAsync(MonsterSpawnData spawnData, CancellationToken cancellationToken)
    {
        int spawnCount = Mathf.Max(0, spawnData.Count);

        for (int i = 0; i < spawnCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpawnMonster(spawnData);

            if (i < spawnCount - 1 && spawnData.SpawnInterval > 0f)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(spawnData.SpawnInterval),
                    cancellationToken: cancellationToken
                );
            }
        }
    }

    private void SpawnMonster(MonsterSpawnData spawnData)
    {
        if (!prefabById.TryGetValue(spawnData.MonsterId, out GameObject prefab))
        {
            Debug.LogWarning($"MonsterSpawnCsv: monster prefab is missing. monsterId={spawnData.MonsterId}", this);
            return;
        }

        SpawnPrefab(prefab);
    }
}

[Serializable]
public class MonsterPrefabEntry
{
    [SerializeField] private string monsterId;
    [SerializeField] private GameObject monsterPrefab;

    public string MonsterId => monsterId;
    public GameObject MonsterPrefab => monsterPrefab;
}
