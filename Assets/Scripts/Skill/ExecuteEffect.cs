using UnityEngine;
using NorthLand.Combat;

// 처형(#318): 감전에 맞은 적에게 표식을 남기고, 표식이 살아 있는 동안 어떤 피해원으로든
// 체력이 임계 이하로 떨어지면 그 순간 처형된다. 레벨업 = 임계 상승.
//
// 화상(BurnEffect)과 같은 "대상 결속" 축이지만 상태 소유자가 다르다 — 화상은 대상의
// StatusEffectHandler에 얹히고, 처형은 Enemy 자체 필드에 실린다.
// 표식은 매 피해마다 읽혀야 하는데 핸들러는 필요할 때 AddComponent로 붙는 컴포넌트라,
// TakeDamage가 피해마다 조회+null 가드를 하게 된다(근거 상세: Enemy.cs의 표식 필드 주석).
public class ExecuteEffect : SkillEffect
{
    [Header("처형 수치")]
    // 레벨별 처형 임계(MaxHp 대비 **비율**). 인덱스 0 = Lv1.
    // 다른 효과처럼 "레벨당 증가분 × 레벨" 곱셈식으로 두지 않고 배열인 이유: 현재 값(0.1/0.2/0.3)은
    // 선형이지만, 레벨 곡선을 비선형으로 바꿀 여지를 코드 변경 없이 남겨두기 위함이다.
    //
    // Range는 오authoring 방지용이다(WL-169) — 비율 자리에 백분율(30)이 들어가면
    // TryExecute의 `HpRatio > executeThreshold`가 영원히 false라 표식이 걸린 모든 비보스 적이
    // 즉사하는데, 컴파일 에러도 콘솔 경고도 없이 씬 diff 한 줄로 지나간다.
    // Unity의 Range는 배열 원소마다 적용되므로 인스펙터에서 오입력 자체가 막힌다.
    [SerializeField, Range(0f, 1f)] float[] thresholdByLevel = { 0.1f, 0.2f, 0.3f };
    [SerializeField, Min(0f)] float markDuration = 2f;

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
                Debug.Log($"[SkillEffect] 처형 표식: 대상={enemy.name}, Lv{Level}, 임계 {threshold * 100f:0.#}%, 지속 {markDuration}s");
        }
    }
}