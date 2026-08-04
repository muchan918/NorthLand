using System.Collections.Generic;
using UnityEngine;

// 웨이브 클리어 보상(#169)을 스킬 특수효과(SkillEffect)로 라우팅하는 씬 싱글톤.
// 보상은 전부 스킬 특수효과 — 타입 분류 없이 전 타입을 효과 컴포넌트로 다룬다.
// 효과들은 이 오브젝트에 컴포넌트로 부착하며, 레벨 소유·스킬 이벤트 구독·발동 로직은
// 각 SkillEffect가 담당한다(SkillEffect.cs 참고). 여기는 보상 → 효과 위임과 레벨 조회만.
public class SkillEffectManager : MonoBehaviour
{
    public static SkillEffectManager Instance { get; private set; }

    // 이 오브젝트에 부착된 효과 컴포넌트들. Awake에서 수집한다.
    readonly Dictionary<WaveRewardType, SkillEffect> effects = new();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            foreach (var effect in GetComponents<SkillEffect>())
            {
                if (!effects.TryAdd(effect.Type, effect))
                    Debug.LogWarning($"[SkillEffect] 같은 타입의 효과가 중복 부착됨: {effect.Type}", effect);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // 보상 선택 결과를 해당 타입의 효과에 위임한다. WaveRewardController가 호출.
    public void ApplyReward(WaveRewardData reward)
    {
        if (reward == null)
        {
            return;
        }

        if (!effects.TryGetValue(reward.RewardType, out var effect))
        {
            Debug.LogWarning($"[SkillEffect] {reward.RewardType} 타입의 SkillEffect 컴포넌트가 부착돼 있지 않아 보상이 무시됨", this);
            return;
        }

        effect.OnRewardApplied(reward.Amount);
    }

    // 현재 효과 레벨. 효과 미부착/미보유 시 0.
    public int GetLevel(WaveRewardType type)
    {
        return effects.TryGetValue(type, out var effect) ? effect.Level : 0;
    }

    // 보상 패널(#287)이 카드에 표시할 수치 줄. 효과 미부착 시 빈 문자열.
    public string GetStatSummary(WaveRewardType type)
    {
        return effects.TryGetValue(type, out var effect) ? effect.GetStatSummary() : string.Empty;
    }
}
