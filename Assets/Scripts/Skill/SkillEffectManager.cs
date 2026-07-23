using System.Collections.Generic;
using UnityEngine;

// 웨이브 클리어 보상으로 얻는 스킬 특수효과(#169)의 레벨 상태 중앙 보관소.
// 보상은 전부 스킬 특수효과이므로 WaveRewardType 전 타입을 레벨 관리 대상으로 다룬다.
// 보상 선택 시 WaveRewardController가 ApplyReward를 호출해 레벨을 올리고,
// 스킬 시전 측(SkillManager 훅 — 이후 단계)은 GetLevel로 현재 레벨을 읽어간다.
// 레벨은 런(run) 단위로만 유효하므로 씬 생명주기 상태로 충분하다.
//
// 새 효과 추가 절차(이슈 #169 확장 방침): WaveRewardType에 값 1개 추가 →
// 효과 구현 단계에서 수치 설정 블록 + 발동 분기 추가.
public class SkillEffectManager : MonoBehaviour
{
    public static SkillEffectManager Instance { get; private set; }

    // 효과별 현재 레벨. 미보유 효과는 키 자체가 없다(= 0레벨).
    readonly Dictionary<WaveRewardType, int> effectLevels = new();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // 보상 선택 결과를 반영해 해당 효과의 레벨을 올린다.
    public void ApplyReward(WaveRewardData reward)
    {
        if (reward == null)
        {
            return;
        }

        effectLevels.TryGetValue(reward.RewardType, out int currentLevel);
        int newLevel = currentLevel + reward.Amount;
        effectLevels[reward.RewardType] = newLevel;

        Debug.Log($"[SkillEffect] {reward.RewardType} Lv{currentLevel} → Lv{newLevel}", reward);
    }

    // 현재 효과 레벨. 미보유 시 0 — 스킬 훅은 이 값 하나만 보고 분기하면 된다.
    public int GetLevel(WaveRewardType type)
    {
        effectLevels.TryGetValue(type, out int level);
        return level;
    }
}
