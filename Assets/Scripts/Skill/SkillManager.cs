using System;
using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;

// 플레이어의 클릭 시전 기본 스킬(#103). 밤에만 시전 가능하며, 클릭한 위치를 중심으로 범위 내
// 몬스터에게 즉시 데미지를 준다. 컨셉은 "감전" — 로직은 단순하게 유지하고 타격감(이펙트/사운드)
// 위주로 튜닝한다. 밸런싱 수치가 미정(GDD §8)이고 스킬이 1개뿐이라 CSV 데이터 파이프라인은
// 쓰지 않고, 아래 값을 인스펙터에서 직접 튜닝한다.
// 보상 특수효과(#169)는 ImpactResolved 이벤트로 얹힌다 — 이 클래스는 어떤 효과가 있는지 모르고,
// 획득한 효과(SkillEffect)가 스스로 구독한다. 구독자가 없으면 기본 감전 그대로.
public class SkillManager : MonoBehaviour
{
    public static SkillManager Instance { get; private set; }

    [Header("감전 스킬 스탯 (타격감 튜닝용)")]
    [SerializeField] float damage = 30f;
    [SerializeField] float radius = 3f;
    [SerializeField] float cooldown = 5f;

    // TODO(TBD): Tower/Enemy와 동일하게 임시 LayerMask 방식. 팀 컨벤션 확정 후 정리(Tower.cs 참고).
    [SerializeField] LayerMask enemyLayerMask;

    [Header("연출")]
    [SerializeField] GameObject impactEffectPrefab;
    [SerializeField] AudioClip impactSfx;

    // 임팩트(착탄) 1회가 끝날 때마다 발행 — 보상으로 획득한 특수효과(SkillEffect)가 구독한다.
    // 컨텍스트의 HitTargets는 임팩트마다 재사용되는 버퍼라 이벤트 처리 중에만 유효.
    public event Action<SkillCastContext> ImpactResolved;

    float cooldownTimer;
    readonly Collider[] hitBuffer = new Collider[16];
    readonly List<IDamageable> hitTargets = new List<IDamageable>(16);

    public float Radius => radius;
    public bool IsReady => cooldownTimer <= 0f;
    public float CooldownRemaining01 => cooldown <= 0f ? 0f : Mathf.Clamp01(cooldownTimer / cooldown);

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

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
    }

    public bool CanCast()
    {
        if (!IsReady) return false;
        if (DayNightManager.Instance != null &&
            DayNightManager.Instance.CurrentPhase != DayNightManager.Phase.Night) return false;
        return true;
    }

    // 클릭한 위치를 중심으로 감전 임팩트를 발동한다. 특수효과가 ExtraImpacts를 가산하면
    // (추가시전) 임팩트가 그만큼 반복되고, 반복분에서도 나머지 효과는 정상 발동한다.
    public bool CastAt(Vector3 position)
    {
        if (!CanCast()) return false;

        var context = new SkillCastContext
        {
            Position = position,
            HitTargets = hitTargets,
        };

        for (int impact = 0; impact <= context.ExtraImpacts; impact++)
        {
            context.ImpactIndex = impact;
            ResolveImpactDamage(position);
            ImpactResolved?.Invoke(context);
        }

        cooldownTimer = cooldown;
        return true;
    }

    // 임팩트 1회: 반경 내 적 전체에게 데미지 적용 + 맞은 적을 hitTargets에 수집 + 연출.
    void ResolveImpactDamage(Vector3 position)
    {
        hitTargets.Clear();

        int count = Physics.OverlapSphereNonAlloc(position, radius, hitBuffer, enemyLayerMask);
        int damagedCount = 0;
        for (int i = 0; i < count; i++)
        {
            var damageable = hitBuffer[i].GetComponentInParent<IDamageable>();
            // Source: 플레이어 스킬은 IAttacker 개체(타워/몬스터)가 아니라 직접 시전이라 null로 둔다.
            // 현재 DamageInfo.Source는 어디서도 역참조하지 않아 안전(StatusEffectHandler.cs 참고).
            if (damageable != null && damageable.Faction == Faction.Enemy && !damageable.IsDead)
            {
                damageable.TakeDamage(new DamageInfo(damage, null));
                damagedCount++;
                if (!damageable.IsDead)   // 즉사한 적은 특수효과 대상에서 제외
                    hitTargets.Add(damageable);
            }
        }

        // 쿨다운이 있어 자주 호출되지 않으므로 로그 스팸 걱정 없이 시전마다 요약을 남긴다(테스트용).
        Debug.Log($"[Skill] 감전 시전: 위치={position}, 적중={damagedCount}마리, 데미지={damage}");

        ApplyImpact(position);
    }

    // 테스트 하네스 전용: 검증용으로 소모한 쿨다운을 즉시 리셋해 인터랙티브 테스트를 바로 이어갈 수 있게 한다.
    public void DebugResetCooldown() => cooldownTimer = 0f;

    void ApplyImpact(Vector3 position)
    {
        if (impactEffectPrefab != null)
            Instantiate(impactEffectPrefab, position, Quaternion.identity);

        if (impactSfx != null)
            AudioSource.PlayClipAtPoint(impactSfx, position);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
