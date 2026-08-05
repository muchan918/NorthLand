using UnityEngine;

// 추가시전(#169): 감전 스킬이 1회 클릭에 총 (1 + 레벨)번 발동한다. 반복분은 동시에 터지지 않고
// repeatInterval 간격을 두고 이어진다("한 번 누르면 잠시 뒤 한 번 더"). 반복 임팩트에서도
// 화상·폭탄 등 다른 효과는 정상 발동하므로 조합 시너지가 생긴다.
public class CountEffect : SkillEffect
{
    [Header("추가시전 (총 발동 횟수 = 1 + 레벨)")]
    [SerializeField] float repeatInterval = 0.5f;   // 반복 발동 사이 간격(초)

    [Header("디버그")]
    [SerializeField] bool debugLog;   // 반복 예약을 Console에 출력 (검증용)

    public override WaveRewardType Type => WaveRewardType.ExtraCast;

    // 보상 패널(#287) 표시용. 총 발동 = 1 + 레벨(HandleImpact의 ExtraImpacts 가산과 같은 정의).
    // 미보유(Lv0)면 1회 — 추가시전 없이 기본 1번 발동한다는 뜻이라 그대로 맞는 값이다.
    public int GetCurrentCastCount() => GetCastCountAt(Level);
    public int GetCastCountAt(int level) => 1 + level;

    public override string GetStatSummary(int levelDelta)
        => SkillStatsFormatter.BuildCastCountLine(GetCurrentCastCount(), GetCastCountAt(Level + levelDelta));

    protected override void HandleImpact(SkillCastContext context)
    {
        // 최초 시전에서만 반복을 예약한다 — 반복분에서 또 가산하면 무한 반복.
        if (context.ImpactIndex != 0) return;

        context.ExtraImpacts += Level;
        context.ExtraImpactInterval = repeatInterval;

        if (debugLog)
            Debug.Log($"[SkillEffect] 추가시전 예약: Lv{Level} → {repeatInterval}초 간격으로 {Level}회 반복");
    }
}
