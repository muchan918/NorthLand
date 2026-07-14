using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MonsterSpawnWaveProvider : MonoBehaviour
{
    [SerializeField] private string spawnTableName = "MonsterSpawnTable";
    [SerializeField] private List<MonsterPrefabEntry> monsterPrefabs = new List<MonsterPrefabEntry>();

    private readonly MonsterSpawnTable spawnTable = new MonsterSpawnTable();
    private readonly List<MonsterSpawnEntry> cachedEntries = new List<MonsterSpawnEntry>();
    private Dictionary<string, GameObject> prefabById = new Dictionary<string, GameObject>();

    private void Awake()
    {
        Load();
    }

    public bool TryGetWave(int round, out IReadOnlyList<MonsterSpawnEntry> entries)
    {
        cachedEntries.Clear();

        foreach (MonsterSpawnData spawnData in spawnTable.GetRound(round))
        {
            if (!prefabById.TryGetValue(spawnData.MonsterId, out GameObject prefab))
            {
                Debug.LogWarning($"MonsterSpawnWaveProvider: monster prefab is missing. monsterId={spawnData.MonsterId}", this);
                continue;
            }

            cachedEntries.Add(new MonsterSpawnEntry(
                prefab,
                spawnData.Count,
                spawnData.StartDelay,
                spawnData.SpawnInterval
            ));
        }

        entries = cachedEntries;
        return cachedEntries.Count > 0;
    }

    private void Load()
    {
        spawnTable.Load(spawnTableName);

        prefabById = monsterPrefabs
            .Where(entry => !string.IsNullOrWhiteSpace(entry.MonsterId) && entry.MonsterPrefab != null)
            .GroupBy(entry => entry.MonsterId)
            .ToDictionary(group => group.Key, group => group.First().MonsterPrefab);
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
