using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using NorthLand.Combat;

// 버프 화상(#169): 버프 스킬 지속시간 동안 타워 투사체 공격에 명중당한 적에게 화상 도트를 부여한다.
// Projectile.DamageDealt 이벤트 기반이라 실제 Projectile을 띄우는 공격만 대상이다 — 이 축을 타지
// 않는 타워는 전부 의도적 제외다: AuraTower(Magic)는 투사체가 없고 애초에 버프 대상도 아니며,
// BeamAction처럼 LineRenderer로 즉시 히트스캔 판정하는 공격(#298 멀티인페르노)도 투사체를 만들지
// 않아 마찬가지로 제외된다. 향후 체이닝·빔 등 LineRenderer 기반 히트스캔 공격을 새로 만들 때도
// 이 축을 타려면 별도로 Projectile.DamageDealt를 발행해야 한다 — 안 하면 이 보상은 자동으로
// 그 타워를 건너뛴다(설계상 의도, 표시 문구 "투사체 타워"와 일치). 레벨업 = 틱 데미지 증가.
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

    // 보상 패널(#287) 표시용. HandleTowerDamage의 실제 계산과 같은 식이라 표시와 실효가 어긋날 수 없다
    // (미보유 = Lv0 = 0).
    public float GetCurrentTickDamage() => GetTickDamageAt(Level);
    public float GetTickDamageAt(int level) => tickDamagePerLevel * level;

    public override string GetStatSummary()
        => SkillStatsFormatter.BuildTickDamageLine(GetCurrentTickDamage(), GetTickDamageAt(NextLevel));

    // #315 — 이 효과는 버프 스킬에 붙는데 그 스킬이 은퇴했다. Instance가 항상 null이라 구독 자체가
    // 불가능해 무해하지만, 씬 병합으로 되살아났을 때 어느 오브젝트인지 지목하기 위해 남긴다.
    void Awake()
    {
        Debug.LogError("[#315] 제거된 버프 화상(BurnBuff)이 씬에 부착돼 있습니다 — 이 컴포넌트를 제거하세요.", this);
    }

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
        if (source is not Tower) return;   // 타워 투사체만 (안전망 — 다른 IAttacker가 투사체를 쓰게 되더라도)
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
