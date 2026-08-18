using NorthLand.Combat;
using System.Collections.Generic;
using UnityEngine;


public readonly struct WaveMonsterCount
{
    public EnemyAsset Asset { get; }
    public int Count { get; }

    public WaveMonsterCount(EnemyAsset asset, int count)
    {
        Asset = asset;
        Count = count;
    }
}

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

    private readonly List<WaveMonsterCount> cachedComposition = new();

    // 등록된 웨이브 개수 = 리스트 마지막 항목의 웨이브 번호 = 최종 웨이브. 등록된 웨이브가 없으면 0.
    public int FinalWaveNumber { get; private set; }

    // 이 웨이브를 클리어하면 게임이 끝나는가(승리 판정용). 웨이브 미등록(0)이면 판정하지 않는다.
    // 최종 번호를 넘어선 라운드도 true로 수렴시켜, 데이터 없는 밤이 무한 반복되지 않게 한다.
    public bool IsFinalWave(int waveNumber) => FinalWaveNumber > 0 && waveNumber >= FinalWaveNumber;

    private void Awake()
    {
        BuildWaveOrder();
    }

    /// <summary>
    /// 웨이브 번호에 해당하는 유효한 스폰 목록을 제공합니다.
    /// MonsterSpawn이 소비하는 공개 API이며,
    /// UI용 몬스터 구성은 TryGetWaveComposition을 사용합니다.
    /// </summary>
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
            if (!TryResolveGroup(waveNumber,wave,group,out GameObject monsterPrefab,out int spawnCount))
            {
                continue;
            }

            cachedEntries.Add(new MonsterSpawnEntry(monsterPrefab,spawnCount,nextGroupStartDelay,spawnInterval));

            nextGroupStartDelay += spawnCount * spawnInterval;
        }

        entries = cachedEntries;
        return cachedEntries.Count > 0;
    }

    public bool TryGetWaveComposition(int waveNumber,out IReadOnlyList<WaveMonsterCount> composition)
    {
        cachedComposition.Clear();

        if (!TryGetWaveAsset(waveNumber, out MonsterWaveAsset wave))
        {
            composition = cachedComposition;
            return false;
        }

        foreach (MonsterWaveGroup group in wave.Groups)
        {
            if (!TryResolveGroup(waveNumber,wave,group,out GameObject monsterPrefab,out int spawnCount))
            {
                continue;
            }

            Enemy enemy = monsterPrefab.GetComponentInChildren<Enemy>(true);
            EnemyAsset asset = enemy != null ? enemy.Asset : null;

            cachedComposition.Add(new WaveMonsterCount(asset, spawnCount));
        }

        composition = cachedComposition;
        return cachedComposition.Count > 0;
    }

    private bool TryResolveGroup(int waveNumber,MonsterWaveAsset wave,MonsterWaveGroup group,out GameObject monsterPrefab,out int spawnCount)
    {
        monsterPrefab = null;
        spawnCount = 0;

        if (group == null)
        {
            return false;
        }

        if (group.MonsterPrefab == null)
        {
            Debug.LogWarning($"Wave {waveNumber}에 몬스터 프리팹이 없는 항목이 있습니다.",wave);

            return false;
        }

        spawnCount = Mathf.Max(0, group.Count);

        if (spawnCount == 0)
        {
            return false;
        }

        monsterPrefab = group.MonsterPrefab;
        return true;
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

    /// <summary>
    /// 웨이브 번호를 내부 orderedWaves 인덱스로 변환해 에셋을 조회합니다.
    /// Provider 내부 API에서만 사용합니다.
    /// </summary>
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
