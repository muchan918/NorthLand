using UnityEngine;

// 보상(#169)으로 얻는 스킬 특수효과의 공통 베이스. SkillEffectManager 오브젝트에 컴포넌트로 부착한다.
// 레벨이 0→1이 되는 순간 스스로 감전 스킬(SkillManager)의 ImpactResolved 이벤트에 구독하고,
// 이후 레벨업(보상 재선택)은 Level 변수만 올라간다. 시전이 일어나면 구독된 효과만 HandleImpact로 호출된다.
// 수치는 다른 스킬과 동일하게 CSV 없이 파생 컴포넌트의 인스펙터 직접 입력.
//
// 새 효과 추가 = 이 클래스 파생 1개 + SkillEffectManager 오브젝트에 부착. 스킬·매니저는 무수정.
public abstract class SkillEffect : MonoBehaviour
{
    public abstract WaveRewardType Type { get; }

    public int Level { get; private set; }

    bool subscribed;

    // 보상 선택 시 SkillEffectManager가 호출한다.
    public void OnRewardApplied(int amount)
    {
        int previousLevel = Level;
        Level += amount;

        // 첫 획득(0→1) 시에만 구독 — 이후 레벨업은 변수 조정만으로 효과가 강해진다.
        if (!subscribed && SkillManager.Instance != null)
        {
            SkillManager.Instance.ImpactResolved += HandleImpact;
            subscribed = true;
        }

        Debug.Log($"[SkillEffect] {Type} Lv{previousLevel} → Lv{Level}", this);
    }

    void OnDestroy()
    {
        if (subscribed && SkillManager.Instance != null)
            SkillManager.Instance.ImpactResolved -= HandleImpact;
    }

    // 감전 임팩트마다 호출된다(구독 이후에만). 효과별 동작은 여기에.
    protected abstract void HandleImpact(SkillCastContext context);
}
