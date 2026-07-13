using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MonsterSpawnWave", menuName = "Monster/Spawn Wave")]
public class MonsterSpawnWave : ScriptableObject
{
    [SerializeField] private int round = 1;
    [SerializeField] private List<MonsterSpawnEntry> entries = new List<MonsterSpawnEntry>();

    public int Round => round;
    public IReadOnlyList<MonsterSpawnEntry> Entries => entries;
}

[System.Serializable]
public class MonsterSpawnEntry
{
    [SerializeField] private GameObject monsterPrefab;
    [SerializeField] private int count = 1;
    [SerializeField] private float startDelay;
    [SerializeField] private float spawnInterval = 1f;

    public GameObject MonsterPrefab => monsterPrefab;
    public int Count => count;
    public float StartDelay => startDelay;
    public float SpawnInterval => spawnInterval;
}