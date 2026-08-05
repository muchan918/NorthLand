using System;
using System.Collections.Generic;
using UnityEngine;

// 하나의 웨이브에 사용할 몬스터 구성과 생성 간격.
// 이 에셋은 "몇 번째 웨이브인가"를 스스로 갖지 않는다 — 진행 순서는 전적으로
// MonsterSpawnWaveProvider.waves 리스트의 등록 순서(1-base)가 결정한다(#294).
[CreateAssetMenu(
    fileName = "MonsterWave",
    menuName = "Monster/Wave")]
public sealed class MonsterWaveAsset : ScriptableObject
{
    [Header("Wave")]
    [Min(0f)]
    [SerializeField]
    private float spawnInterval = 1f;

    [Header("Monsters")]
    [SerializeField]
    private List<MonsterWaveGroup> groups = new List<MonsterWaveGroup>();

    [Header("Reward")]
    [SerializeField]
    private WaveRewardPool rewardPool;

    public float SpawnInterval => spawnInterval;
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

    public GameObject MonsterPrefab =>monsterPrefab;

    public int Count =>count;
}