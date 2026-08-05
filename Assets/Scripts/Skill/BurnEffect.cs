using UnityEngine;
using NorthLand.Combat;

// 화상(#169): 감전 임팩트에 맞은 적에게 지속 도트 데미지. 레벨업 = 틱 데미지 증가.
// DoT 상태 소유는 대상의 StatusEffectHandler — AuraTower의 부여 패턴 재사용(Combat 코드 무수정).
public class BurnEffect : SkillEffect
{
    [Header("화상 수치 (틱 데미지 = 레벨 x 레벨당 틱 데미지)")]
    [SerializeField] float tickDamagePerLevel = 5f;
    [SerializeField] float tickInterval = 0.5f;
    [SerializeField] float duration = 3f;

    [Header("디버그")]
    [SerializeField] bool debugLog;   // 화상 부여 + 대상의 DoT 틱을 Console에 출력 (검증용)

    // StatusEffectHandler의 effectId 공간을 타워 오라(TowerID 해시)와 공유하므로 고정 문자열 해시로 구분.
    static readonly int EffectId = "skill_burn".GetHashCode();

    public override WaveRewardType Type => WaveRewardType.Burn;

    // 보상 패널(#287) 표시용. HandleImpact의 실제 계산과 같은 식이라 표시와 실효가 어긋날 수 없다
    // (미보유 = Lv0 = 0).
    public float GetCurrentTickDamage() => GetTickDamageAt(Level);
    public float GetTickDamageAt(int level) => tickDamagePerLevel * level;

    public override string GetStatSummary()
        => SkillStatsFormatter.BuildTickDamageLine(GetCurrentTickDamage(), GetTickDamageAt(NextLevel));

    protected override void HandleImpact(SkillCastContext context)
    {
        float damagePerTick = tickDamagePerLevel * Level;

        for (int i = 0; i < context.HitTargets.Count; i++)
        {
            var target = context.HitTargets[i];
            if (target == null || target.IsDead) continue;
            if (target is not Component targetComponent) continue;

            var handler = targetComponent.GetComponent<StatusEffectHandler>();
            if (handler == null) handler = targetComponent.gameObject.AddComponent<StatusEffectHandler>();

            handler.debugLog = debugLog;
            // Source: 플레이어 스킬은 IAttacker 개체가 아니라 null (SkillManager의 DamageInfo와 동일 규약).
            handler.ApplyOrRefresh(EffectId, damagePerTick, tickInterval, duration, null);

            if (debugLog)
                Debug.Log($"[SkillEffect] 화상 부여: 대상={targetComponent.name}, Lv{Level}, 틱당 {damagePerTick} / {tickInterval}s, 지속 {duration}s");
        }
    }
}
