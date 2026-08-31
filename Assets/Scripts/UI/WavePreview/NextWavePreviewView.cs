using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;
using TMPro;

namespace NorthLand.UI
{
    public sealed class NextWavePreviewView : MonoBehaviour
    {
        [Header("Wave Data")]
        [SerializeField] private MonsterSpawnWaveProvider waveProvider;
        [SerializeField] private MonsterSpawn monsterSpawn;

        [Header("UI")]
        [SerializeField] private Transform content;
        [SerializeField] private NextWaveMonsterEntry entryPrefab;
        [SerializeField] private Sprite unknownMonsterIcon;
        [SerializeField] private GameObject noMoreWavesView;

        [SerializeField] private GameObject hpBuff;
        [SerializeField] private TMP_Text hpText;
        [SerializeField] private GameObject speedBuff;
        [SerializeField] private TMP_Text speedText;

        private bool canShowCommonBuff;

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

            canShowCommonBuff =monsterSpawn != null &&hpBuff != null &&hpText != null &&speedBuff != null &&speedText != null;

            if (!canShowCommonBuff)
            {
                Debug.LogWarning("[웨이브 미리보기] 공통 강화 UI 배선이 불완전하여 강화 표시만 생략합니다.",this);
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

            if (hpBuff != null)
            {
                hpBuff.SetActive(false);
            }

            if (speedBuff != null)
            {
                speedBuff.SetActive(false);
            }

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

            bool hasRegularMonster = false;
            bool hasAnyMonster = false;

            foreach (WaveMonsterCount monster in composition)
            {
                bool isValidMonster = monster.Asset != null && monster.Count > 0;

                bool isBoss = monster.Asset != null && monster.Asset.EnemyType == EnemyType.Boss;

                if (isValidMonster)
                {
                    hasAnyMonster = true;
                }

                if (isValidMonster && !isBoss)
                {
                    hasRegularMonster = true;
                }

                AddEntry(monster.Asset != null ? monster.Asset.Icon : null,monster.Count,isBoss);
            }

            if (canShowCommonBuff)
            {
                RefreshCommonBuff(currentWaveNumber,hasRegularMonster,hasAnyMonster);
            }
        }

        private void ShowNoMoreWaves()
        {
            if (noMoreWavesView != null)
            {
                noMoreWavesView.SetActive(true);
            }
        }

        private void AddEntry(Sprite icon, int count, bool isBoss)
        {
            if (content == null || entryPrefab == null)
                return;

            NextWaveMonsterEntry entry = Instantiate(entryPrefab, content);
            entry.Bind(icon != null ? icon : unknownMonsterIcon, count, isBoss);

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

        private void RefreshCommonBuff(int waveNumber,bool hasRegularMonster,bool hasAnyMonster)
        {
            if (!canShowCommonBuff)
            {
                return;
            }

            float hpScale = monsterSpawn.GetWaveHpScale(waveNumber);
            float speedScale = monsterSpawn.GetWaveMoveSpeedScale(waveNumber);

            bool showHp = hasRegularMonster && hpScale > 1f;

            bool showSpeed = hasAnyMonster && speedScale > 1f;

            hpBuff.SetActive(showHp);
            speedBuff.SetActive(showSpeed);

            if (showHp)
            {
                int percent = Mathf.RoundToInt((hpScale - 1f) * 100f);
                hpText.text = $"+{percent}%";
            }

            if (showSpeed)
            {
                int percent = Mathf.RoundToInt((speedScale - 1f) * 100f);
                speedText.text = $"+{percent}%";
            }
        }
    }
}
