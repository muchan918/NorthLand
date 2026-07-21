using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class WaveRewardController : MonoBehaviour
{
    [SerializeField]
    private WaveRewardSelectionUI selectionUI;

    public async UniTask ShowRewardSelectionAsync(WaveRewardPool rewardPool,CancellationToken cancellationToken)
    {
        if (rewardPool == null)
        {
            return;
        }

        if (selectionUI == null)
        {
            Debug.LogWarning("WaveRewardSelectionUI가 연결되지 않았습니다.",this);

            return;
        }

        List<WaveRewardData> candidates = rewardPool.GetRandomCandidates(3);

        if (candidates.Count == 0)
        {
            Debug.LogWarning("선택 가능한 웨이브 보상이 없습니다.",rewardPool);

            return;
        }

        WaveRewardData selectedReward = await selectionUI.SelectRewardAsync(candidates,cancellationToken);

        if (selectedReward == null)
        {
            return;
        }

        GrantReward(selectedReward);
    }

    private void GrantReward(WaveRewardData reward)
    {
        if (reward == null)
        {
            return;
        }

        Debug.Log($"[웨이브 보상 선택]이름: {reward.DisplayName},종류: {reward.RewardType},수치: {reward.Amount}",reward);
    }
}