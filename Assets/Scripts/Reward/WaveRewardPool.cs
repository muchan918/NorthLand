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

    // 풀에 등록된 유효 보상 수. 후보가 0인 이유(풀이 빔 vs 전부 만렙)를 호출부가 구분할 때 쓴다.
    public int ValidRewardCount
    {
        get
        {
            int count = 0;

            foreach (WaveRewardData reward in rewards)
            {
                if (reward != null)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public List<WaveRewardData> GetRandomCandidates
        (int count, System.Random random, System.Func<WaveRewardData, bool> canOffer = null)
    {
        List<WaveRewardData> candidates = new();
        List<WaveRewardData> remaining = new();

        if (random == null)
        {
            Debug.LogError("보상 후보 추출용 Random이 null입니다.", this);

            return candidates;
        }

        foreach (WaveRewardData reward in rewards)
        {
            if (reward == null)
            {
                continue;
            }

            // 만렙 등으로 더 줄 게 없는 보상은 후보에서 제외한다(#292).
            // 판정 기준은 호출부가 소유한다 — SO가 씬 싱글톤(SkillEffectManager)을 알지 않도록.
            if (canOffer != null && !canOffer(reward))
            {
                continue;
            }

            remaining.Add(reward);
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