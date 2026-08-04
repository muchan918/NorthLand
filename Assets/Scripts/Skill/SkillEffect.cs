using UnityEngine;

// 보상(#169)으로 얻는 스킬 특수효과의 공통 베이스. SkillEffectManager 오브젝트에 컴포넌트로 부착한다.
// 레벨이 0→1이 되는 순간 스스로 스킬 이벤트에 구독하고(어느 스킬의 이벤트인지는 TrySubscribe가 결정 —
// 기본은 감전의 ImpactResolved, 버프 계열 효과는 override로 BuffSkillManager 쪽에 구독),
// 이후 레벨업(보상 재선택)은 Level 변수만 올라간다. 시전이 일어나면 구독된 효과만 호출된다.
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
        // (구독 대상 매니저가 아직 없으면 다음 보상 때 재시도)
        if (!subscribed && TrySubscribe())
            subscribed = true;

        Debug.Log($"[SkillEffect] {Type} Lv{previousLevel} → Lv{Level}", this);
    }

    // 보상 패널(#287)에 표시할 "현재 → 획득 후" 수치 줄. 서식·라벨은 SkillStatsFormatter가 소유한다.
    //
    // levelDelta는 호출부가 보상(WaveRewardData.Amount)에서 그대로 받아 넘긴다 — 표시부가 "한 번에 1씩
    // 오른다"고 가정하면 안 된다. 실제 가산은 OnRewardApplied의 amount이고 그 값은 보상 SO가 소유하므로,
    // 하드코딩하면 SO 수치를 바꾸는 순간 카드가 조용히 거짓 수치를 보여준다.
    public abstract string GetStatSummary(int levelDelta);

    // 어느 스킬의 이벤트에 붙을지는 파생이 정한다. 기본: 감전(SkillManager.ImpactResolved).
    protected virtual bool TrySubscribe()
    {
        if (SkillManager.Instance == null) return false;
        SkillManager.Instance.ImpactResolved += HandleImpact;
        return true;
    }

    protected virtual void Unsubscribe()
    {
        if (SkillManager.Instance != null)
            SkillManager.Instance.ImpactResolved -= HandleImpact;
    }

    void OnDestroy()
    {
        if (subscribed)
            Unsubscribe();
    }

    // 감전 임팩트마다 호출된다(기본 구독 대상일 때). 감전 계열 효과는 이걸 override,
    // 다른 스킬에 붙는 효과(예: BurnBuff)는 TrySubscribe/Unsubscribe를 override하고 이건 무시.
    protected virtual void HandleImpact(SkillCastContext context) { }
}
