using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class MonsterSpawn : MonoBehaviour
{
    [System.Serializable]
    private class MonsterPrefabEntry
    {
        public string monsterId = string.Empty;
        public GameObject prefab = null;
    }

    [System.Serializable]
    private class MonsterSpawnPointEntry
    {
        public string spawnPointId = "default";
        public Transform point = null;
    }

    [Header("Data")]
    [SerializeField] private string spawnTableName = "MonsterSpawnTable";
    [SerializeField] private bool playOnStart;
    [SerializeField] private int startRound = 1;

    [Header("References")]
    [SerializeField] private List<MonsterPrefabEntry> monsterPrefabs = new List<MonsterPrefabEntry>();
    [SerializeField] private List<MonsterSpawnPointEntry> spawnPoints = new List<MonsterSpawnPointEntry>();
    [SerializeField] private Transform monsterParent;

    private readonly MonsterSpawnTable spawnTable = new MonsterSpawnTable();
    private readonly Dictionary<string, GameObject> prefabById = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, Transform> spawnPointById = new Dictionary<string, Transform>();

    private bool hasGeneratedSpawnPoint;
    private Vector3 generatedSpawnPosition;
    private Quaternion generatedSpawnRotation = Quaternion.identity;
    private CancellationTokenSource spawnCancellationTokenSource;

    private void Awake()
    {
        BuildPrefabLookup();
        BuildSpawnPointLookup();
        spawnTable.Load(spawnTableName);
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

        CancelSpawnTasks();
        spawnCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            this.GetCancellationTokenOnDestroy()
        );

        SpawnRoundAsync(round, spawnCancellationTokenSource.Token).Forget();
    }

    private async UniTaskVoid SpawnRoundAsync(int round, CancellationToken cancellationToken)
    {
        try
        {
            List<MonsterSpawnData> roundSpawnList = spawnTable.GetRound(round);

            if (roundSpawnList.Count == 0)
            {
                Debug.LogWarning($"MonsterSpawn: round {round} spawn data is empty.", this);
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
                    await UniTask.Delay(TimeSpan.FromSeconds(waitTime), cancellationToken: cancellationToken);
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
            Debug.LogError($"MonsterSpawn: monster prefab is missing. monsterId={spawnData.MonsterId}", this);
            return;
        }

        if (hasGeneratedSpawnPoint)
        {
            Instantiate(prefab, generatedSpawnPosition, generatedSpawnRotation, monsterParent);
            return;
        }

        if (!spawnPointById.TryGetValue(spawnData.SpawnPointId, out Transform spawnPoint))
        {
            Debug.LogError($"MonsterSpawn: spawn point is missing. spawnPointId={spawnData.SpawnPointId}", this);
            return;
        }

        Instantiate(prefab, spawnPoint.position, spawnPoint.rotation, monsterParent);
    }

    private void BuildPrefabLookup()
    {
        prefabById.Clear();

        foreach (MonsterPrefabEntry entry in monsterPrefabs)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.monsterId) || entry.prefab == null)
            {
                continue;
            }

            if (!prefabById.TryAdd(entry.monsterId, entry.prefab))
            {
                Debug.LogError($"MonsterSpawn: duplicate monsterId. monsterId={entry.monsterId}", this);
            }
        }
    }

    private void BuildSpawnPointLookup()
    {
        spawnPointById.Clear();

        foreach (MonsterSpawnPointEntry spawnPoint in spawnPoints)
        {
            if (spawnPoint == null || string.IsNullOrWhiteSpace(spawnPoint.spawnPointId) || spawnPoint.point == null)
            {
                continue;
            }

            if (!spawnPointById.TryAdd(spawnPoint.spawnPointId, spawnPoint.point))
            {
                Debug.LogError($"MonsterSpawn: duplicate spawnPointId. spawnPointId={spawnPoint.spawnPointId}", this);
            }
        }
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