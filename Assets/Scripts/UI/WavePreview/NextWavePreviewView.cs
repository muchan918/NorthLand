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
        [SerializeField] private GameObject noMoreWavesView;

        private readonly List<NextWaveMonsterEntry> spawnedEntries = new();

        private DayNightManager dayNightManager;

        private void Start()
        {
            dayNightManager = DayNightManager.Instance;

            if (dayNightManager == null)
            {
                Debug.LogError("[웨이브 미리보기] DayNightManager를 찾을 수 없습니다.",this);
                enabled = false;
                return;
            }

            if (waveProvider == null)
            {
                Debug.LogError("[웨이브 미리보기] waveProvider가 연결되지 않았습니다.",this);
                enabled = false;
                return;
            }

            if (content == null)
            {
                Debug.LogError("[웨이브 미리보기] content가 연결되지 않았습니다.",this);
                enabled = false;
                return;
            }

            if (entryPrefab == null)
            {
                Debug.LogError("[웨이브 미리보기] entryPrefab이 연결되지 않았습니다.",this);
                enabled = false;
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

            if (noMoreWavesView != null)
            {
                noMoreWavesView.SetActive(false);
            }

            int currentWaveNumber = dayNightManager.CurrentWave;

            if (waveProvider.FinalWaveNumber <= 0)
            {
                Debug.LogError("[웨이브 미리보기] 등록된 웨이브가 없습니다.",this);
                return;
            }

            if (currentWaveNumber > waveProvider.FinalWaveNumber)
            {
                ShowNoMoreWaves();
                return;
            }

            if (!waveProvider.TryGetWaveComposition(currentWaveNumber,out IReadOnlyList<WaveMonsterCount> composition))
            {
                Debug.LogError($"[웨이브 미리보기] {currentWaveNumber}웨이브 구성을 가져오지 못했습니다.",this);
                return;
            }

            foreach (WaveMonsterCount monster in composition)
            {
                AddEntry(monster.Asset != null ? monster.Asset.Icon : null,monster.Count);
            }
        }

        private void ShowNoMoreWaves()
        {
            if (noMoreWavesView != null)
            {
                noMoreWavesView.SetActive(true);
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
