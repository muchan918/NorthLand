using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using NorthLand.Combat;

// 버프 화상(#169): 버프 스킬 지속시간 동안 **투사체 타워**의 명중을 받은 적에게 화상 도트를 부여한다.
// 레벨업 = 틱 데미지 증가.
//
// 구독하는 `Projectile.DamageDealt`는 전달 방식과 무관한 "타워의 직접 타격" 통지라 히트스캔 체인의
// 홉도 함께 올라온다(#252). 화상은 그 중 **투사체 명중만** 받는다 — HandleTowerDamage에서 거른다:
//  · 테마: 체인 타워는 번개(전격)라 발화가 성립하지 않는다.
//  · 수치: 체인은 1발에 최대 MaxChainTargets명(현재 4)을 때리므로, 거르지 않으면 한 번 시전에
//    화상 처리량이 최대 4배가 된다. "1발 = 1명" 전제로 잡은 틱 데미지 튜닝이 조용히 무너진다.
// 발행부(ChainResolver)를 막지 않고 여기서 거르는 이유: DamageDealt는 범용 통지이고, 발행을 막으면
// 앞으로 붙을 다른 구독자까지 체인 명중을 못 보게 된다.
//
// 오라 타워(Magic)는 애초에 이 통지를 발행하지 않아 자연 제외되며, 버프 대상도 아니다.
// 버프 대상 스냅샷은 하지 않는다(단순성 우선) — 창이 열린 동안 새로 지은 타워의 명중에도 화상이 붙는다.
public class BurnBuff : SkillEffect
{
    [Header("버프 화상 수치 (틱 데미지 = 레벨 x 레벨당 틱 데미지)")]
    [SerializeField] float tickDamagePerLevel = 5f;
    [SerializeField] float tickInterval = 0.5f;
    [SerializeField] float duration = 3f;

    [Header("디버그")]
    [SerializeField] bool debugLog;   // 창 개폐/화상 부여를 Console에 출력 (검증용)

    // StatusEffectHandler의 effectId 공간을 타워 오라(TowerID 해시)·감전 화상(skill_burn)과 공유하므로 분리.
    static readonly int EffectId = "buff_burn".GetHashCode();

    float windowEndTime;
    bool windowActive;

    public override WaveRewardType Type => WaveRewardType.BuffBurn;

    // 이 효과는 감전이 아니라 버프 스킬에 붙는다.
    protected override bool TrySubscribe()
    {
        if (BuffSkillManager.Instance == null) return false;
        BuffSkillManager.Instance.BuffResolved += HandleBuffCast;
        return true;
    }

    protected override void Unsubscribe()
    {
        if (BuffSkillManager.Instance != null)
            BuffSkillManager.Instance.BuffResolved -= HandleBuffCast;
        DeactivateWindow();   // 파괴 시 static 이벤트(DamageDealt)에 죽은 구독자가 남지 않도록
    }

    void HandleBuffCast(BuffCastContext context)
    {
        windowEndTime = Time.time + context.Duration;

        // 창이 이미 열려 있으면(버프 재시전) 종료 시각만 연장 — 이중 구독 방지.
        if (!windowActive)
        {
            windowActive = true;
            Projectile.DamageDealt += HandleTowerDamage;
            WindowAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        if (debugLog)
            Debug.Log($"[SkillEffect] 버프 화상 창 열림: {context.Duration}초 동안 타워 명중에 화상 (Lv{Level})");
    }

    async UniTaskVoid WindowAsync(CancellationToken cancellationToken)
    {
        // 재시전으로 endTime이 늘어날 수 있어 고정 Delay 대신 시각 기준으로 대기한다.
        while (Time.time < windowEndTime)
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

        DeactivateWindow();

        if (debugLog)
            Debug.Log("[SkillEffect] 버프 화상 창 종료");
    }

    void DeactivateWindow()
    {
        if (!windowActive) return;
        windowActive = false;
        Projectile.DamageDealt -= HandleTowerDamage;
    }

    void HandleTowerDamage(IAttacker source, IDamageable target)
    {
        if (source is not Tower tower) return;   // 안전망 — 다른 IAttacker가 이 통지를 쓰게 되더라도

        // **투사체 타워만**. AttackBehaviour는 전달 축이 투사체인 구현체이므로, 여기서는 능력 인터페이스
        // (IAttackBehaviour)가 아니라 **구상 클래스로 묻는 것이 의도**다 — 히트스캔 체인을 제외해야 한다.
        // 클래스 주석의 두 근거(번개 테마 / 1발 최대 4명) 참조.
        if (!tower.Has<AttackBehaviour>()) return;

        if (target == null || target.IsDead) return;
        if (target is not Component targetComponent) return;

        var handler = targetComponent.GetComponent<StatusEffectHandler>();
        if (handler == null) handler = targetComponent.gameObject.AddComponent<StatusEffectHandler>();

        float damagePerTick = tickDamagePerLevel * Level;
        handler.debugLog = debugLog;
        // Source: 스킬 계열 효과는 null 규약(SkillManager/BurnEffect와 동일). 타워를 넘기지 않는 이유는
        // 화상의 출처가 타워가 아니라 플레이어의 보상 효과이기 때문.
        handler.ApplyOrRefresh(EffectId, damagePerTick, tickInterval, duration, null);

        if (debugLog)
            Debug.Log($"[SkillEffect] 버프 화상 부여: 대상={targetComponent.name}, Lv{Level}, 틱당 {damagePerTick}");
    }
}
