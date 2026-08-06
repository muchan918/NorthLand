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
    [Header("공통")]
    [SerializeField, Min(1)] int maxLevel = 3;   // 도달 시 보상 후보에서 제외된다(#292)

    public abstract WaveRewardType Type { get; }

    public int Level { get; private set; }

    public int MaxLevel => maxLevel;
    public bool IsMaxLevel => Level >= maxLevel;

    bool subscribed;

    // 보상 선택 시 SkillEffectManager가 호출한다. 한 번 선택 = 1레벨이며,
    // 상한에 걸리면 더 오르지 않는다(#292 — 만렙 효과는 애초에 후보에서 빠지지만 안전망으로 둔다).
    public void OnRewardApplied()
    {
        int previousLevel = Level;
        Level = NextLevel;

        // 첫 획득(0→1) 시에만 구독 — 이후 레벨업은 변수 조정만으로 효과가 강해진다.
        // (구독 대상 매니저가 아직 없으면 다음 보상 때 재시도)
        if (!subscribed && TrySubscribe())
            subscribed = true;

        Debug.Log($"[SkillEffect] {Type} Lv{previousLevel} → Lv{Level}", this);
    }

    /// <summary>
    /// 저장된 효과 레벨을 절대값으로 복원한다.
    /// 일반 보상 획득 경로처럼 기존 레벨에 더하지 않는다.
    /// </summary>
    /// <param name="level">복원할 효과 레벨.</param>
    /// <returns>레벨과 이벤트 구독 복원에 성공하면 true.</returns>
    public bool TryRestoreLevel(int level)
    {
        if (level < 0 || level > maxLevel)
        {
            Debug.LogError(
                $"[SkillEffect] 효과 레벨이 유효 범위를 벗어났습니다: " +
                $"{Type}={level}, 허용=0~{maxLevel}",
                this);

            return false;
        }

        // 효과를 보유한 상태라면 먼저 필요한 스킬 이벤트에 연결한다.
        if (level > 0 && !subscribed)
        {
            if (!TrySubscribe())
            {
                Debug.LogError($"[SkillEffect] 효과 이벤트 구독에 실패했습니다: {Type}",this);

                return false;
            }

            subscribed = true;
        }

        // 0레벨로 복원하는 경우 기존 구독을 제거한다.
        if (level == 0 && subscribed)
        {
            Unsubscribe();
            subscribed = false;
        }

        Level = level;
        return true;
    }

    public int NextLevel => Mathf.Min(Level + 1, maxLevel);

    // 다음 레벨이 상한인가 — 보상 카드가 "Lv 2 → Max"로 표시할지 판단한다(#292).
    public bool NextIsMaxLevel => NextLevel >= maxLevel;

    // 보상 패널(#287)에 표시할 "현재 → 획득 후" 수치 줄. 서식·라벨은 SkillStatsFormatter가 소유한다.
    // 파생은 Level과 NextLevel로만 계산한다 — 증가폭은 보상 SO가 아니라 레벨 규칙이 소유하므로
    // 표시와 실효가 어긋날 여지가 없다.
    public abstract string GetStatSummary();

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
