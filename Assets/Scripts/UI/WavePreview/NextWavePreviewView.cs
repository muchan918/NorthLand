using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;

public sealed class NextWavePreviewView : MonoBehaviour
{
    [Header("Wave Data")]
    [SerializeField] private MonsterSpawnWaveProvider waveProvider;

    [Header("UI")]
    [SerializeField] private Transform content;
    [SerializeField] private NextWaveMonsterEntry entryPrefab;
    [SerializeField] private Sprite unknownMonsterIcon;

    private readonly List<NextWaveMonsterEntry> spawnedEntries = new();
    private readonly Dictionary<EnemyAsset, int> monsterCounts = new();
    private readonly List<EnemyAsset> monsterOrder = new();

    private DayNightManager dayNightManager;

    private void Start()
    {
        dayNightManager = DayNightManager.Instance;

        if (dayNightManager == null)
        {
            Debug.LogWarning("DayNightManager was not found.");
            return;
        }

        dayNightManager.OnDayStart += HandleDayStart;

        Refresh();
    }

    private void OnDestroy()
    {
        if (dayNightManager == null)
        {
            return;
        }

        dayNightManager.OnDayStart -= HandleDayStart;
    }

    private void HandleDayStart()
    {
        Refresh();
    }

    private void Refresh()
    {
        ClearEntries();

        if (waveProvider == null || dayNightManager == null)
        {
            return;
        }

        int nextWaveNumber = dayNightManager.CurrentWave;

        if (!waveProvider.TryGetWaveAsset(nextWaveNumber,out MonsterWaveAsset waveAsset) ||waveAsset == null)
        {
            return;
        }

        monsterCounts.Clear();
        monsterOrder.Clear();

        int unknownMonsterCount = 0;

        if (waveAsset.Groups == null)
        {
            return;
        }

        foreach (MonsterWaveGroup group in waveAsset.Groups)
        {
            if (group == null ||group.MonsterPrefab == null ||group.Count <= 0)
            {
                continue;
            }

            Enemy enemy = group.MonsterPrefab.GetComponentInChildren<Enemy>(true);

            EnemyAsset enemyAsset = enemy != null ? enemy.Asset: null;

            if (enemyAsset == null)
            {
                unknownMonsterCount += group.Count;
                continue;
            }

            if (!monsterCounts.ContainsKey(enemyAsset))
            {
                monsterCounts.Add(enemyAsset, 0);
                monsterOrder.Add(enemyAsset);
            }

            monsterCounts[enemyAsset] += group.Count;
        }

        foreach (EnemyAsset enemyAsset in monsterOrder)
        {
            AddEntry(enemyAsset.Icon,monsterCounts[enemyAsset]);
        }

        if (unknownMonsterCount > 0)
        {
            AddEntry(null, unknownMonsterCount);
        }
    }

    private void AddEntry(Sprite icon, int count)
    {
        if (content == null || entryPrefab == null)
        {
            return;
        }

        NextWaveMonsterEntry entry =
            Instantiate(entryPrefab, content);

        entry.Bind(icon != null ? icon : unknownMonsterIcon,count);

        spawnedEntries.Add(entry);
    }

    private void ClearEntries()
    {
        foreach (NextWaveMonsterEntry entry in spawnedEntries)
        {
            if (entry == null)
            {
                continue;
            }

            entry.gameObject.SetActive(false);
            Destroy(entry.gameObject);
        }

        spawnedEntries.Clear();
    }
}
