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

    // 등록된 웨이브 중 가장 큰 번호 = 최종 웨이브. 등록된 웨이브가 없으면 0.
    public int FinalWaveNumber { get; private set; }

    // 이 웨이브를 클리어하면 게임이 끝나는가(승리 판정용). 웨이브 미등록(0)이면 판정하지 않는다.
    // 최종 번호를 넘어선 라운드도 true로 수렴시켜, 데이터 없는 밤이 무한 반복되지 않게 한다.
    public bool IsFinalWave(int waveNumber) => FinalWaveNumber > 0 && waveNumber >= FinalWaveNumber;

    private void Awake()
    {
        BuildWaveLookup();
    }

    // 웨이브 번호에 해당하는 스폰 목록 제공
    public bool TryGetWave(int waveNumber,out IReadOnlyList<MonsterSpawnEntry> entries)
    {
        cachedEntries.Clear();

        if (!waveByNumber.TryGetValue(waveNumber,out MonsterWaveAsset wave))
        {
            entries = cachedEntries;
            return false;
        }

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
        FinalWaveNumber = 0;

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

            FinalWaveNumber = Mathf.Max(FinalWaveNumber, waveNumber);
        }

    }
    public bool TryGetRewardPool(int waveNumber,out WaveRewardPool rewardPool)
    {
        rewardPool = null;

        if (!waveByNumber.TryGetValue(waveNumber,out MonsterWaveAsset wave))
        {
            return false;
        }

        rewardPool = wave.RewardPool;
        return rewardPool != null;
    }

}
