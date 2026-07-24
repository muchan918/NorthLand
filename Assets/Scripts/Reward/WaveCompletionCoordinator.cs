using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using NorthLand.Core;

public class WaveCompletionCoordinator : MonoBehaviour
{
    [SerializeField]
    private MonsterSpawn monsterSpawn;

    [SerializeField]
    private MonsterSpawnWaveProvider waveProvider;

    [SerializeField]
    private WaveRewardController rewardController;

    private bool isCompletingWave;

    private void OnEnable()
    {
        if (monsterSpawn != null)
        {
            monsterSpawn.WaveCleared += HandleWaveCleared;
        }
    }

    private void OnDisable()
    {
        if (monsterSpawn != null)
        {
            monsterSpawn.WaveCleared -= HandleWaveCleared;
        }
    }

    private void HandleWaveCleared(int waveNumber)
    {
        CompleteWaveAsync(waveNumber,this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTask CompleteWaveAsync(int waveNumber,CancellationToken cancellationToken)
    {
        if (isCompletingWave)
        {
            return;
        }

        isCompletingWave = true;

        try
        {
            // 최종 웨이브 클리어 = 게임 승리(GDD §3.4). 보상 3택1도 EndNight도 건너뛰고 밤 상태 그대로
            // 승리 화면으로 간다 — 런이 끝나는 마당의 스킬 강화와 다음 날 정산은 의미가 없다.
            if (waveProvider != null &&waveProvider.IsFinalWave(waveNumber))
            {
                if (GameManager.Instance != null)
                {
                    Debug.Log($"[웨이브] 최종 웨이브({waveNumber}) 클리어 — 게임 승리",this);

                    GameManager.Instance.TriggerVictory();
                    return;
                }

                // GameManager 미배선 시 소프트락 대신 기존 흐름(보상→EndNight)으로 폴백한다.
                Debug.LogWarning("[웨이브] 최종 웨이브지만 GameManager가 없어 승리 화면을 표시하지 못했습니다.",this);
            }

            if (waveProvider != null &&waveProvider.TryGetRewardPool(waveNumber,out WaveRewardPool rewardPool))
            {
                if (rewardController != null)
                {
                    await rewardController.ShowRewardSelectionAsync(rewardPool,cancellationToken);
                }
                else
                {
                    Debug.LogWarning($"Wave {waveNumber}에 보상이 있지만 RewardController가 없습니다.",this);
                }
            }

            DayNightManager dayNight = DayNightManager.Instance;

            if (dayNight != null &&dayNight.CurrentPhase == DayNightManager.Phase.Night)
            {
                dayNight.EndNight();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            isCompletingWave = false;
        }
    }
}