using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// 하나의 웨이브에 사용할 몬스터 구성과 생성 간격.
// 이 에셋은 "몇 번째 웨이브인가"를 스스로 갖지 않는다.
// 진행 순서는 MonsterSpawnWaveProvider.waves 리스트 등록 순서가 결정한다.
[CreateAssetMenu(
    fileName = "MonsterWave",
    menuName = "Monster/Wave")]
public sealed class MonsterWaveAsset : ScriptableObject
{
    [Header("Wave")]
    [FormerlySerializedAs("spawnInterval")]
    [Tooltip("각 스폰 배치 사이의 최소 대기 시간(초)")]
    [Min(0f)]
    [SerializeField]
    private float minSpawnInterval = 0.3f;

    [Tooltip("각 스폰 배치 사이의 최대 대기 시간(초)")]
    [Min(0f)]
    [SerializeField]
    private float maxSpawnInterval = 1f;

    [Tooltip("한 배치에 동시에 생성할 수 있는 최대 몬스터 수. 실제 수량은 매 배치마다 1~이 값 사이에서 무작위로 결정됩니다.")]
    [Min(1)]
    [SerializeField]
    private int spawnCountPerBatch = 1;

    [Tooltip("일반 몬스터의 생성 순서를 무작위로 섞을지 여부")]
    [SerializeField]
    private bool randomizeSpawnOrder = true;

    [Header("Monsters")]
    [SerializeField]
    private List<MonsterWaveGroup> groups = new List<MonsterWaveGroup>();

    [Header("Reward")]
    [SerializeField]
    private WaveRewardPool rewardPool;

    public float MinSpawnInterval => minSpawnInterval;
    public float MaxSpawnInterval => maxSpawnInterval;
    public int SpawnCountPerBatch => spawnCountPerBatch;
    public bool RandomizeSpawnOrder => randomizeSpawnOrder;
    public List<MonsterWaveGroup> Groups => groups;

    public WaveRewardPool RewardPool => rewardPool;
    public bool HasReward => rewardPool != null;
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

    public GameObject MonsterPrefab => monsterPrefab;
    public int Count => count;
}