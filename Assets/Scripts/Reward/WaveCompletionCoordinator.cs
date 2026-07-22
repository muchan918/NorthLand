using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

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