using UnityEngine;

public class MonsterSpawnEntry
{
    public MonsterSpawnEntry(GameObject monsterPrefab, int count, float startDelay, float spawnInterval)
    {
        MonsterPrefab = monsterPrefab;
        Count = count;
        StartDelay = startDelay;
        SpawnInterval = spawnInterval;
    }

    public GameObject MonsterPrefab { get; }
    public int Count { get; }
    public float StartDelay { get; }
    public float SpawnInterval { get; }
}
