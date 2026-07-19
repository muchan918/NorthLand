using System;
using System.Collections.Generic;
using UnityEngine;

// 하나의 웨이브에 사용할 몬스터 구성과 생성 간격
[CreateAssetMenu(
    fileName = "MonsterWave",
    menuName = "Monster/Wave")]
public sealed class MonsterWaveAsset : ScriptableObject
{
    [Header("Wave")]
    [Min(1)]
    [SerializeField]
    private int waveNumber = 1;

    [Min(0f)]
    [SerializeField]
    private float spawnInterval = 1f;

    [Header("Monsters")]
    [SerializeField]
    private List<MonsterWaveGroup> groups = new List<MonsterWaveGroup>();

    public int WaveNumber => waveNumber;

    public float SpawnInterval => spawnInterval;

    public List<MonsterWaveGroup> Groups => groups;
}

// 웨이브에 포함되는 몬스터 한 종류와 수량
[Serializable]
public sealed class MonsterWaveGroup
{
    [SerializeField]
    private GameObject monsterPrefab;

    [Min(1)]
    [SerializeField]
    private int count = 1;

    public GameObject MonsterPrefab =>monsterPrefab;

    public int Count =>count;
}