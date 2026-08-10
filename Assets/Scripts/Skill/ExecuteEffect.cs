using UnityEngine;
using NorthLand.Combat;

// 처형(#318): 감전에 맞은 적에게 표식을 남기고, 표식이 살아 있는 동안 어떤 피해원으로든
// 체력이 임계 이하로 떨어지면 그 순간 처형된다. 레벨업 = 임계 상승.
//
// 화상(BurnEffect)과 같은 "대상 결속" 축이지만 상태 소유자가 다르다 — 화상은 대상의
// StatusEffectHandler에 DoT를 얹고, 처형은 Enemy 자체 필드에 실린다.
// 피해 판정 자체를 바꾸는 상태라 TakeDamage 안에서 읽혀야 하기 때문이다.
public class ExecuteEffect : SkillEffect
{
    [Header("처형 수치")]
    // 레벨별 처형 임계(MaxHp 대비 비율). 인덱스 0 = Lv1.
    // 다른 효과의 "레벨당 증가분 × 레벨" 선형식을 쓰지 않는 이유: 8→16→25가 비선형이다.
    [SerializeField] float[] thresholdByLevel = { 0.1f, 0.2f, 0.3f };
    [SerializeField] float markDuration = 2f;

    [Header("디버그")]
    [SerializeField] bool debugLog;   // 표식 부여 + 집행 순간을 Console에 출력 (검증용)

    public override WaveRewardType Type => WaveRewardType.Execute;

    // 보상 패널(#287) 표시용. HandleImpact의 실제 계산과 같은 식이라 표시와 실효가 어긋날 수 없다
    // (미보유 = Lv0 = 0).
    public float GetCurrentThreshold() => GetThresholdAt(Level);

    public float GetThresholdAt(int level)
    {
        if (level <= 0 || thresholdByLevel == null || thresholdByLevel.Length == 0) return 0f;

        // 배열이 maxLevel보다 짧게 authoring돼도 마지막 값으로 클램프한다 —
        // 인스펙터에서 원소를 지웠을 때 IndexOutOfRange로 시전 자체가 죽는 것보다 낫다.
        return thresholdByLevel[Mathf.Min(level, thresholdByLevel.Length) - 1];
    }

    public override string GetStatSummary()
        => SkillStatsFormatter.BuildExecuteThresholdLine(GetCurrentThreshold(), GetThresholdAt(NextLevel));

    protected override void HandleImpact(SkillCastContext context)
    {
        float threshold = GetCurrentThreshold();

        for (int i = 0; i < context.HitTargets.Count; i++)
        {
            var target = context.HitTargets[i];
            if (target == null || target.IsDead) continue;

            // IDamageable이 아니라 구체 타입 Enemy로 캐스트하는 이유: MarkForExecute/IsBoss는
            // IDamageable에 없고, 인터페이스에 넣으면 PlayerBase·Soldier까지 무의미한 계약을
            // 구현해야 한다. SkillManager.ResolveImpactDamage가 이미 Faction.Enemy만 걸러
            // HitTargets에 담으므로 여기 들어오는 건 사실상 Enemy뿐이며 이 검사는 안전망이다.
            //
            // 보스 제외를 MarkForExecute가 아니라 여기서 거는 이유: TakeDamage 경로에
            // 처형과 무관한 조건을 심지 않기 위함이다(Enemy.MarkForExecute 주석 참조).
            if (target is not Enemy enemy || enemy.IsBoss) continue;

            enemy.MarkForExecute(threshold, markDuration, debugLog);

            if (debugLog)
                Debug.Log($"[SkillEffect] 처형 표식: 대상={enemy.name}, Lv{Level}, 임계 {threshold:P0}, 지속 {markDuration}s");
        }
    }
}