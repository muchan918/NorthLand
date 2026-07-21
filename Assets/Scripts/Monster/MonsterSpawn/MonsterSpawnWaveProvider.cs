using System.Collections.Generic;
using UnityEngine;

// Wave SO를 MonsterSpawnEntry로 변환하여 제공
public sealed class MonsterSpawnWaveProvider :
    MonoBehaviour
{
    [Header("Wave Assets")]
    [SerializeField]
    private List<MonsterWaveAsset> waves = new List<MonsterWaveAsset>();

    private readonly Dictionary<int, MonsterWaveAsset> waveByNumber = new Dictionary<int, MonsterWaveAsset>();

    private readonly List<MonsterSpawnEntry> cachedEntries = new List<MonsterSpawnEntry>();

    private void Awake()
    {
        BuildWaveLookup();
    }

    // 웨이브 번호에 해당하는 스폰 목록 제공
    public bool TryGetWave(int waveNumber,out IReadOnlyList<MonsterSpawnEntry> entries,out WaveRewardPool rewardPool)
    {
        cachedEntries.Clear();
        rewardPool = null;

        if (!waveByNumber.TryGetValue(waveNumber,out MonsterWaveAsset wave))
        {
            entries = cachedEntries;
            return false;
        }

        rewardPool = wave.RewardPool;

        float nextGroupStartDelay = 0f;
        float spawnInterval = Mathf.Max(0f, wave.SpawnInterval);

        foreach (MonsterWaveGroup group in wave.Groups)
        {
            if (group == null)
            {
                continue;
            }

            if (group.MonsterPrefab == null)
            {
                Debug.LogWarning($"Wave {waveNumber}에 몬스터 프리팹이 없는 항목이 있습니다.",wave);

                continue;
            }

            int spawnCount = Mathf.Max(0, group.Count);

            if (spawnCount == 0)
            {
                continue;
            }

            cachedEntries.Add(
                new MonsterSpawnEntry(group.MonsterPrefab,spawnCount,nextGroupStartDelay,spawnInterval)
            );

            nextGroupStartDelay += spawnCount * spawnInterval;
        }

        entries = cachedEntries;
        return cachedEntries.Count > 0;
    }

    // 웨이브 번호로 빠르게 찾을 수 있도록 Dictionary 생성
    private void BuildWaveLookup()
    {
        waveByNumber.Clear();

        foreach (MonsterWaveAsset wave in waves)
        {
            if (wave == null)
            {
                continue;
            }

            int waveNumber = wave.WaveNumber;

            if (waveNumber < 1)
            {
                Debug.LogWarning($"{wave.name}의 Wave Number는 1 이상이어야 합니다.", wave);

                continue;
            }

            if (waveByNumber.ContainsKey(waveNumber))
            {
                Debug.LogWarning($"Wave Number {waveNumber}가 중복되었습니다. 먼저 등록된 SO를 사용합니다.", this);

                continue;
            }

            waveByNumber.Add(waveNumber, wave);
        }
    }

}
