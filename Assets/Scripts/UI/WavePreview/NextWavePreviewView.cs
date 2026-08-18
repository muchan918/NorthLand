using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;

namespace NorthLand.UI
{
    public sealed class NextWavePreviewView : MonoBehaviour
    {
        [Header("Wave Data")]
        [SerializeField] private MonsterSpawnWaveProvider waveProvider;

        [Header("UI")]
        [SerializeField] private Transform content;
        [SerializeField] private NextWaveMonsterEntry entryPrefab;
        [SerializeField] private Sprite unknownMonsterIcon;

        private readonly List<NextWaveMonsterEntry> spawnedEntries = new();

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

            int currentWaveNumber = dayNightManager.CurrentWave;

            if (!waveProvider.TryGetWaveComposition(currentWaveNumber, out IReadOnlyList<WaveMonsterCount> composition))
            {
                return;
            }

            foreach (WaveMonsterCount monster in composition)
            {
                AddEntry(monster.Asset != null ? monster.Asset.Icon : null, monster.Count);
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

            entry.Bind(icon != null ? icon : unknownMonsterIcon, count);

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
}
