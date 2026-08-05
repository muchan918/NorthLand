using System.Collections.Generic;
using UnityEngine;

// Wave SO를 MonsterSpawnEntry로 변환하여 제공.
// 웨이브 진행 순서 = waves 리스트의 등록 순서(1-base 인덱스). 마지막 항목이 최종 웨이브다(#294).
public sealed class MonsterSpawnWaveProvider :
    MonoBehaviour
{
    [Header("Wave Assets")]
    [SerializeField]
    private List<MonsterWaveAsset> waves = new List<MonsterWaveAsset>();

    // 인스펙터 waves에서 null 슬롯만 걸러낸 런타임 진행 순서. 인덱스 0 = 1웨이브.
    private readonly List<MonsterWaveAsset> orderedWaves = new List<MonsterWaveAsset>();

    private readonly List<MonsterSpawnEntry> cachedEntries = new List<MonsterSpawnEntry>();

    // 등록된 웨이브 개수 = 리스트 마지막 항목의 웨이브 번호 = 최종 웨이브. 등록된 웨이브가 없으면 0.
    public int FinalWaveNumber { get; private set; }

    // 이 웨이브를 클리어하면 게임이 끝나는가(승리 판정용). 웨이브 미등록(0)이면 판정하지 않는다.
    // 최종 번호를 넘어선 라운드도 true로 수렴시켜, 데이터 없는 밤이 무한 반복되지 않게 한다.
    public bool IsFinalWave(int waveNumber) => FinalWaveNumber > 0 && waveNumber >= FinalWaveNumber;

    private void Awake()
    {
        BuildWaveOrder();
    }

    // 웨이브 번호(1-base = waves 리스트에서 몇 번째인가)에 해당하는 스폰 목록 제공
    public bool TryGetWave(int waveNumber,out IReadOnlyList<MonsterSpawnEntry> entries)
    {
        cachedEntries.Clear();

        if (!TryGetWaveAsset(waveNumber,out MonsterWaveAsset wave))
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

    // 리스트 등록 순서를 그대로 진행 순서로 삼는다.
    // null 슬롯은 웨이브가 아니라 authoring 노이즈로 보고 제외·압축한다 — 웨이브는 빈 밤 없이
    // 순차적으로 이어져야 하므로, 빈 슬롯 뒤의 웨이브가 한 칸씩 당겨지고 진행 웨이브 수도 그만큼 줄어든다.
    private void BuildWaveOrder()
    {
        orderedWaves.Clear();

        foreach (MonsterWaveAsset wave in waves)
        {
            if (wave == null)
            {
                Debug.LogWarning("waves 리스트에 비어 있는 슬롯이 있어 건너뜁니다.", this);

                continue;
            }

            orderedWaves.Add(wave);
        }

        FinalWaveNumber = orderedWaves.Count;
    }

    // 웨이브 번호(1-base)를 리스트 인덱스로 변환한다. 범위 밖이면 false.
    // 1-base ↔ 0-base 변환은 이 한 곳에만 둔다.
    private bool TryGetWaveAsset(int waveNumber, out MonsterWaveAsset wave)
    {
        if (waveNumber < 1 || waveNumber > orderedWaves.Count)
        {
            wave = null;
            return false;
        }

        wave = orderedWaves[waveNumber - 1];
        return true;
    }

    public bool TryGetRewardPool(int waveNumber,out WaveRewardPool rewardPool)
    {
        rewardPool = null;

        if (!TryGetWaveAsset(waveNumber,out MonsterWaveAsset wave))
        {
            return false;
        }

        rewardPool = wave.RewardPool;
        return rewardPool != null;
    }

}
