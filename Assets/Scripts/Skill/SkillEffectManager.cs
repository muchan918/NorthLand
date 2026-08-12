using System.Collections.Generic;
using UnityEngine;

// 보상 카드 한 장을 그리는 데 필요한 레벨·수치를 한 번에 담아 넘기는 값(#353).
//
// 도입 전에는 표시부가 GetLevel/GetNextLevel/ReachesMaxLevel/GetStatSummary를 각각 조회했고,
// 카드면 색을 정하는 WaveRewardSelectionUI가 다섯 번째로 또 조회했다. 조회가 흩어져 있으니
// "이 표시 요소는 현재 레벨을 쓰는가 다음 레벨을 쓰는가"를 호출부마다 따로 정하게 됐고,
// 실제로 별은 다음 레벨·레벨 줄은 현재 레벨로 갈렸다(#353이 고친 증상). 두 값이 이름으로
// 갈라진 한 덩어리로 오면 그 선택이 호출부에서 드러난다.
//
// 효과가 부착돼 있지 않으면 default가 그대로 "효과 없음"을 뜻한다 — Stats가 null이라
// 표시부의 빈 문자열 게이트가 분기 없이 걸리고, SkillEffectManager.Instance가 null인
// 경우도 호출부가 default를 쓰면 같은 경로로 흐른다.
public readonly struct SkillEffectSnapshot
{
    public SkillEffectSnapshot(int level, int nextLevel, bool nextIsMax, string stats)
    {
        Level = level;
        NextLevel = nextLevel;
        NextIsMax = nextIsMax;
        Stats = stats;
    }

    /// 지금 보유한 레벨. 미보유·미부착은 0.
    public int Level { get; }

    /// 이 보상을 고르면 도달할 레벨(상한에서 잘림). 미부착은 0.
    public int NextLevel { get; }

    /// 이번 선택으로 상한에 닿는가 — 카드의 "Lv 2 → Max" 표기용(#292).
    public bool NextIsMax { get; }

    /// 카드에 표시할 "현재 → 획득 후" 수치 줄(평문, 여러 줄 가능). 미부착은 null.
    public string Stats { get; }
}

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

        effect.OnRewardApplied();
    }

    // 현재 효과 레벨. 효과 미부착/미보유 시 0.
    public int GetLevel(WaveRewardType type)
    {
        return effects.TryGetValue(type, out var effect) ? effect.Level : 0;
    }

    // 보상 카드 한 장에 필요한 값 전부(#353). 표시부는 이걸 한 번 받아 별·카드면 색·레벨 줄·
    // 수치 줄을 전부 같은 값에서 그린다 — 값마다 따로 조회하면 어느 요소가 현재 레벨을 쓰고
    // 어느 요소가 다음 레벨을 쓰는지가 호출부에 흩어진다(SkillEffectSnapshot 주석 참고).
    // 효과 미부착 시 default — Level 0 / NextLevel 0 / Stats null.
    public SkillEffectSnapshot GetSnapshot(WaveRewardType type)
    {
        return effects.TryGetValue(type, out var effect)
            ? new SkillEffectSnapshot(effect.Level, effect.NextLevel, effect.NextIsMaxLevel, effect.GetStatSummary())
            : default;
    }

    // 이 타입이 이미 최대 레벨인가(#292). 효과 미부착 시 false —
    // 배선 누락을 필터가 조용히 삼키지 않도록 후보에 남겨 ApplyReward의 경고가 뜨게 한다.
    public bool IsMaxLevel(WaveRewardType type)
    {
        return effects.TryGetValue(type, out var effect) && effect.IsMaxLevel;
    }

    /// <summary>
    /// 저장된 효과 레벨을 해당 효과 컴포넌트에 복원한다.
    /// </summary>
    public bool TryRestoreLevel(WaveRewardType type,int level)
    {
        if (!effects.TryGetValue(type,out SkillEffect effect))
        {
            Debug.LogError($"[SkillEffect] 복원할 효과 컴포넌트가 없습니다: {type}",this);

            return false;
        }

        return effect.TryRestoreLevel(level);
    }

    /// <summary>
    /// 해당 보상 효과 컴포넌트가 등록되어 있는가.
    /// </summary>
    public bool HasEffect(WaveRewardType type)
    {
        return effects.ContainsKey(type);
    }
}
