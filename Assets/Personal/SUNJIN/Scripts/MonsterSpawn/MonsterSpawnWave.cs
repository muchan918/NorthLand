using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Monster/Spawn Wave")]
public class MonsterSpawnWave : ScriptableObject
{
    public int round;
    public List<MonsterSpawnEntry> entries;
}

[System.Serializable]
public class MonsterSpawnEntry
{
    public GameObject monsterPrefab;
    public int count;
    public float startDelay;
    public float spawnInterval;
}