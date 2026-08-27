using NorthLand.Combat;
using NorthLand.Core;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

    [Header("Tutorial Waves")]
    [Tooltip("튜토리얼 모드에서만 사용하는 웨이브. 정상 게임 진행에는 관여하지 않는다.")]
    [SerializeField]
    private List<MonsterWaveAsset> tutorialWaves = new List<MonsterWaveAsset>();

    [Tooltip("[에디터 테스트용] 켜면 튜토리얼 웨이브 구성만 사용한다. 초기 자원·적 HP·스킬 쿨다운·튜토리얼 UI에는 영향을 주지 않는다.")]
    [SerializeField]
    [FormerlySerializedAs("forceTutorialMode")]
    private bool forceTutorialWaves;

    [Header("Seed")]
    [Tooltip("몬스터 스폰 랜덤에 사용할 마스터 시드를 제공하는 RunBootstrapper")]
    [SerializeField]
    private RunBootstrapper runBootstrapper;

    // 인스펙터 waves에서 null 슬롯만 걸러낸 런타임 진행 순서. 인덱스 0 = 1웨이브.
    private readonly List<MonsterWaveAsset> orderedWaves = new List<MonsterWaveAsset>();

    private readonly List<MonsterSpawnEntry> cachedEntries = new List<MonsterSpawnEntry>();

    private readonly List<GameObject> normalSpawnPrefabs = new List<GameObject>();

    private readonly List<GameObject> bossSpawnPrefabs = new List<GameObject>();

    private readonly List<WaveMonsterCount> cachedComposition = new();

    // 등록된 웨이브 개수 = 리스트 마지막 항목의 웨이브 번호 = 최종 웨이브. 등록된 웨이브가 없으면 0.
    public int FinalWaveNumber { get; private set; }

    // 이 웨이브를 클리어하면 게임이 끝나는가(승리 판정용). 웨이브 미등록(0)이면 판정하지 않는다.
    // 최종 번호를 넘어선 라운드도 true로 수렴시켜, 데이터 없는 밤이 무한 반복되지 않게 한다.
    // 튜토리얼 웨이브는 게임 승리에 관여하지 않는다. 마지막 튜토리얼 웨이브를 깨도
    // WaveCompletionCoordinator가 승리 화면을 띄우지 않게 여기서 막는다.
    // FinalWaveNumber를 0으로 두는 방식은 쓰지 않는다 — NextWavePreviewView가
    // "등록된 웨이브가 없습니다" 에러를 계속 찍는다.
    public bool IsFinalWave(int waveNumber) =>
        !usesTutorialWaves && FinalWaveNumber > 0 && waveNumber >= FinalWaveNumber;

    private readonly List<WaveMonsterCount> cachedBossComposition = new();

    // 이번 실행이 튜토리얼인지. Awake에서 한 번 확정하고 이후 바뀌지 않는다 —
    // 웨이브 순서가 이미 만들어진 뒤에 모드가 뒤집히면 진행 번호와 리스트가 어긋난다.
    private bool usesTutorialWaves;

    private void Awake()
    {
        usesTutorialWaves = forceTutorialWaves || TutorialMode.IsActive;

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
        normalSpawnPrefabs.Clear();
        bossSpawnPrefabs.Clear();

        if (!TryGetWaveAsset(waveNumber,out MonsterWaveAsset wave))
        {
            entries = cachedEntries;
            return false;
        }

        // 웨이브 그룹의 수량을 개별 프리팹 목록으로 펼치고,
        // 일반 몬스터와 보스를 분리한다.
        CollectSpawnPrefabs(waveNumber, wave);

        System.Random waveRandom = CreateWaveRandom(waveNumber);

        // 일반 몬스터만 랜덤으로 섞는다.
        // 보스 목록은 섞지 않고 항상 일반 몬스터 뒤에 배치한다.
        if (wave.RandomizeSpawnOrder)
        {
            Shuffle(normalSpawnPrefabs, waveRandom);
        }

        // 기존 에셋이나 잘못된 Inspector 값이 0이어도
        // 최소 한 마리씩 생성되도록 보정한다.
        int spawnCountPerBatch = Mathf.Max(1, wave.SpawnCountPerBatch);

        float intraBatchJitter = Mathf.Max(0f, wave.IntraBatchJitter);

        // 음수 인터벌을 방지한다.
        float minSpawnInterval = Mathf.Max(0f, wave.MinSpawnInterval);

        // 최대값이 최소값보다 작으면 최소값으로 보정한다.
        float maxSpawnInterval =Mathf.Max(minSpawnInterval,wave.MaxSpawnInterval);

        float nextStartDelay = AddEntriesByBatch(normalSpawnPrefabs,spawnCountPerBatch,intraBatchJitter,minSpawnInterval,maxSpawnInterval,0f,waveRandom);

        if (normalSpawnPrefabs.Count > 0 &&
            bossSpawnPrefabs.Count > 0)
        {
            nextStartDelay += RandomRange(waveRandom,minSpawnInterval,maxSpawnInterval);
        }

        AddEntriesByBatch(bossSpawnPrefabs,spawnCountPerBatch,intraBatchJitter,minSpawnInterval,maxSpawnInterval,nextStartDelay,waveRandom);
        entries = cachedEntries;
        return cachedEntries.Count > 0;
    }
    public bool TryGetWaveComposition(int waveNumber, out IReadOnlyList<WaveMonsterCount> composition)
    {
        cachedComposition.Clear();
        cachedBossComposition.Clear();

        if (!TryGetWaveAsset(waveNumber, out MonsterWaveAsset wave))
        {
            composition = cachedComposition;
            return false;
        }

        foreach (MonsterWaveGroup group in wave.Groups)
        {
            if (!TryResolveGroup(waveNumber, wave, group, out GameObject monsterPrefab, out int spawnCount))
            {
                continue;
            }

            Enemy enemy = monsterPrefab.GetComponent<Enemy>();
            EnemyAsset asset = enemy != null ? enemy.Asset : null;
            WaveMonsterCount count = new WaveMonsterCount(asset, spawnCount);

            (enemy != null && enemy.IsBoss? cachedBossComposition: cachedComposition).Add(count);
        }

        cachedComposition.AddRange(cachedBossComposition);

        composition = cachedComposition;
        return cachedComposition.Count > 0;
    }
    private void CollectSpawnPrefabs(int waveNumber,MonsterWaveAsset wave)
    {
        foreach (MonsterWaveGroup group in wave.Groups)
        {
            if (!TryResolveGroup(waveNumber,wave,group,out GameObject monsterPrefab,out int spawnCount))
            {
                continue;
            }

            bool isBoss = IsBossPrefab(monsterPrefab);

            List<GameObject> targetList =isBoss? bossSpawnPrefabs: normalSpawnPrefabs;

            for (int i = 0; i < spawnCount; i++)
            {
                targetList.Add(monsterPrefab);
            }
        }
    }

    private static bool IsBossPrefab(GameObject monsterPrefab)
    {
        Enemy enemy = monsterPrefab != null? monsterPrefab.GetComponent<Enemy>(): null;

        return enemy != null && enemy.IsBoss;
    }

    private static void Shuffle(List<GameObject> prefabs,System.Random random)
    {
        for (int i = prefabs.Count - 1; i > 0; i--)
        {
            int randomIndex = random.Next(0, i + 1);

            (prefabs[i], prefabs[randomIndex]) = (prefabs[randomIndex], prefabs[i]);
        }
    }

    private static float RandomRange(System.Random random,float min,float max)
    {
        if (max <= min)
        {
            return min;
        }

        return min + (float)random.NextDouble() * (max - min);
    }

    private float AddEntriesByBatch(IReadOnlyList<GameObject> prefabs,int maxSpawnCountPerBatch,float intraBatchJitter,float minSpawnInterval,float maxSpawnInterval,float initialStartDelay,
     System.Random random)
    {
        float currentStartDelay = initialStartDelay;
        int currentIndex = 0;

        while (currentIndex < prefabs.Count)
        {
            int remainingCount = prefabs.Count - currentIndex;

            int batchCount = random.Next(1,maxSpawnCountPerBatch + 1);

            batchCount = Mathf.Min(batchCount,remainingCount);

            int batchEnd = currentIndex + batchCount;

            for (int i = currentIndex; i < batchEnd; i++)
            {
                cachedEntries.Add(new MonsterSpawnEntry(prefabs[i],1,currentStartDelay + (i - currentIndex) * intraBatchJitter,0f));
            }

            currentIndex = batchEnd;

            currentStartDelay += (batchCount - 1) * intraBatchJitter;

            if (currentIndex < prefabs.Count)
            {
                currentStartDelay += RandomRange(random,minSpawnInterval,maxSpawnInterval);
            }
        }

        return currentStartDelay;
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

        List<MonsterWaveAsset> source = usesTutorialWaves ? tutorialWaves : waves;

        foreach (MonsterWaveAsset wave in source)
        {
            if (wave == null)
            {
                Debug.LogWarning(
                    usesTutorialWaves
                        ? "tutorialWaves 리스트에 비어 있는 슬롯이 있어 건너뜁니다."
                        : "waves 리스트에 비어 있는 슬롯이 있어 건너뜁니다.",
                    this);

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

    private System.Random CreateWaveRandom(int waveNumber)
    {
        if (runBootstrapper == null)
        {
            Debug.LogWarning("[MonsterSpawn] RunBootstrapper 참조가 없어 씬에서 검색합니다.",this);

            runBootstrapper = FindFirstObjectByType<RunBootstrapper>();
        }

        int masterSeed = runBootstrapper != null ? runBootstrapper.MasterSeed : 0;

        if (masterSeed <= 0)
        {
            Debug.LogError($"[MonsterSpawn] 마스터 시드를 찾을 수 없어 웨이브 {waveNumber} 번호를 임시 시드로 사용합니다.",this);

            masterSeed = waveNumber;
        }

        int waveSeed = RunSeedDeriver.Derive(masterSeed,RunSeedDeriver.MonsterSpawnWaveTag(waveNumber));

        return new System.Random(waveSeed);
    }

}
