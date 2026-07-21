using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "WaveRewardPool",
    menuName = "Scriptable Objects/Wave Reward Pool"
)]
public class WaveRewardPool : ScriptableObject
{
    [SerializeField]
    private List<WaveRewardData> rewards = new();

    public List<WaveRewardData> GetRandomCandidates(int count,System.Random random)
    {
        List<WaveRewardData> candidates = new();
        List<WaveRewardData> remaining = new();

        if (random == null)
        {
            Debug.LogError("보상 후보 추출용 Random이 null입니다.",this);

            return candidates;
        }

        foreach (WaveRewardData reward in rewards)
        {
            if (reward != null)
            {
                remaining.Add(reward);
            }
        }

        int candidateCount = Mathf.Min(count, remaining.Count);

        for (int i = 0; i < candidateCount; i++)
        {
            int randomIndex = random.Next(remaining.Count);

            candidates.Add(remaining[randomIndex]);
            remaining.RemoveAt(randomIndex);
        }

        return candidates;
    }
}