using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using NorthLand.Combat;
using NorthLand.Core;

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
    [Tooltip("마법 연구소 레벨별 착탄 이펙트. 비우거나 해당 레벨 엔트리가 없으면 impactEffectPrefab 사용")]
    [SerializeField] SkillVisualSet _visualSet;

    // 마법 연구소(#205) — 레벨 비례로 기본 스탯(damage/radius/cooldown)을 배율 강화한다.
    // 컨트롤러는 레벨(int)만 노출하고, 레벨→배율 매핑은 `_magicLabAsset.Skill.UpgradeLevels`(SO)에
    // authoring한다 — 비용과 배율이 같은 리스트라 레벨 개수가 어긋날 수 없다(PR#216 리뷰, 씬 리스트 제거).
    // 보상 특수효과(#169, SkillEffect.Level)와는 독립 축 — ImpactResolved 구독 흐름은 건드리지 않는다.
    [Header("마법 연구소 강화 (#205)")]
    [Tooltip("비우면 강화 없음(레벨 0 취급)")]
    [SerializeField] BuildingAsset _magicLabAsset;
    [Tooltip("비우면 씬에서 자동 탐색")]
    [SerializeField] ManagementController _managementController;

    // 임팩트(착탄) 1회가 끝날 때마다 발행 — 보상으로 획득한 특수효과(SkillEffect)가 구독한다.
    // 컨텍스트의 HitTargets는 임팩트마다 재사용되는 버퍼라 이벤트 처리 중에만 유효.
    public event Action<SkillCastContext> ImpactResolved;

    float cooldownTimer;
    readonly Collider[] hitBuffer = new Collider[16];
    readonly List<IDamageable> hitTargets = new List<IDamageable>(16);

    // 예약된 추가 착탄(RepeatImpactsAsync) 취소용. 파괴 토큰과 링크해 기존 파괴-취소 동작을 유지하며,
    // 웨이브 종료(밤→낮) 시 Cancel해 낮으로 넘어간 반복 착탄이 뒤늦게 발동하지 않게 한다(#200 ②).
    CancellationTokenSource repeatCts;

    // 마법 연구소 레벨로 계산한 유효 스탯(레벨 0/미배선이면 기본값과 동일).
    float effectiveDamage;
    float effectiveRadius;
    float effectiveCooldown;
    int lastMagicLabLevel = -1; // 레벨 변경 시에만 로그를 남기기 위한 캐시(-1: 최초 1회는 무조건 로그).

    // 레벨별 착탄 이펙트. 레벨이 바뀔 때만 조회하면 되므로 RefreshUpgrade에서 캐싱한다
    // (effectiveDamage 등을 미리 계산해 두는 것과 같은 이유).
    SkillVisualSet.LevelVisual _currentVisual;

    public float Radius => effectiveRadius;
    public bool IsReady => cooldownTimer <= 0f;
    public float CooldownRemaining01 => effectiveCooldown <= 0f ? 0f : Mathf.Clamp01(cooldownTimer / effectiveCooldown);

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

    void Start()
    {
        // 웨이브 종료(밤→낮) 시 예약된 추가 착탄을 취소한다. DayNightManager가 없으면 취소만 못 할 뿐
        // 스킬 자체는 동작하므로 경고만 남긴다(PhasePanelSwitcher.Start 패턴).
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnNightToDay += HandleWaveEnd;
        else
            Debug.LogWarning("[Skill] DayNightManager를 찾을 수 없어 웨이브 종료 시 추가 착탄 취소가 배선되지 않았습니다.");

        // 승리/게임오버는 EndNight()(→OnNightToDay)를 타지 않으므로 결과 확정 신호로도 취소한다(#200 리뷰).
        // 그러지 않으면 예약된 추가 착탄이 결과 화면 뒤에서 계속 발동한다.
        if (GameManager.Instance != null)
            GameManager.Instance.OnResultDecided += HandleResultDecided;

        // 마법 연구소(#205) — 비워두면 씬에서 자동 탐색(BuildingInfoUI와 동일 관례).
        if (_managementController == null)
            _managementController = FindFirstObjectByType<ManagementController>();
        if (_managementController != null)
            _managementController.OnChanged += RefreshUpgrade;
        RefreshUpgrade();
    }

    void OnDestroy()
    {
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnNightToDay -= HandleWaveEnd;
        if (GameManager.Instance != null)
            GameManager.Instance.OnResultDecided -= HandleResultDecided;
        if (_managementController != null)
            _managementController.OnChanged -= RefreshUpgrade;

        repeatCts?.Cancel();
        repeatCts?.Dispose();
        repeatCts = null;
    }

    // 마법 연구소 레벨(미배선·미보유 시 0)로 유효 스탯을 다시 계산한다. 레벨 0/범위 밖 = 배율 1.0(기본값 그대로).
    void RefreshUpgrade()
    {
        int level = (_managementController != null && _magicLabAsset != null)
            ? _managementController.GetUpgradeLevel(_magicLabAsset)
            : 0;

        List<BuildingAsset.SkillUpgradeLevel> levels = _magicLabAsset != null ? _magicLabAsset.Skill?.UpgradeLevels : null;
        if (levels != null && levels.Count > 0 && level > 0)
        {
            // 레벨이 테이블 크기를 넘으면(비정상 상태 — 컨트롤러가 레벨을 이 SO에서 산출하므로 실제로는
            // 발생하지 않지만) base로 되돌리지 않고 마지막 엔트리를 유지한다(PR#216 리뷰, 방어적 clamp).
            BuildingAsset.SkillUpgradeLevel scaling = levels[Mathf.Clamp(level, 1, levels.Count) - 1];
            effectiveDamage = damage * PositiveOr1(scaling.DamageMultiplier);
            effectiveRadius = radius * PositiveOr1(scaling.RadiusMultiplier);
            effectiveCooldown = cooldown * PositiveOr1(scaling.CooldownMultiplier);
        }
        else
        {
            effectiveDamage = damage;
            effectiveRadius = radius;
            effectiveCooldown = cooldown;
        }

        _currentVisual = _visualSet != null ? _visualSet.Resolve(level) : null;

        if (level != lastMagicLabLevel)
        {
            Debug.Log($"[Skill] 마법 연구소 Lv{level} 적용 — 감전 데미지={effectiveDamage}(x{effectiveDamage / damage:F2}), 사거리={effectiveRadius}(x{effectiveRadius / radius:F2}), 쿨다운={effectiveCooldown}(x{effectiveCooldown / cooldown:F2})");
            lastMagicLabLevel = level;
        }
    }

    // BuildingAsset.SkillUpgradeLevel의 배율 필드 기본값이 1이라 이 헬퍼는 대부분 no-op이지만, 과거
    // 데이터나 실수로 0/음수가 들어와도 1.0(배율 없음)으로 취급해 방어한다(PR#216 리뷰) — 쿨다운 0=무한
    // 연발, 사거리 0=미적중 같은 조용한 파괴적 결과를 막는다.
    static float PositiveOr1(float multiplier) => multiplier > 0f ? multiplier : 1f;

    // 웨이브 종료: 예약된 추가 착탄을 취소한다. 다음 시전에서 CastAt이 새 링크 소스를 만든다.
    void HandleWaveEnd()
    {
        repeatCts?.Cancel();
        repeatCts?.Dispose();
        repeatCts = null;
    }

    // 승리/게임오버(런 종료)도 웨이브 종료와 동일하게 진행 중 효과를 취소한다.
    void HandleResultDecided(GameResult _) => HandleWaveEnd();

    void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
    }

    public bool CanCast()
    {
        if (GameManager.Instance != null && GameManager.Instance.Result != GameResult.Playing) return false;
        if (!IsReady) return false;
        if (DayNightManager.Instance != null &&
            DayNightManager.Instance.CurrentPhase != DayNightManager.Phase.Night) return false;
        return true;
    }

    // 클릭한 위치를 중심으로 감전 임팩트를 발동한다. 특수효과가 ExtraImpacts를 가산하면
    // (추가시전) ExtraImpactInterval 간격으로 임팩트가 반복되고, 반복분에서도 나머지 효과는
    // 정상 발동한다(화상/폭탄 조합 시너지).
    public bool CastAt(Vector3 position)
    {
        if (!CanCast()) return false;

        var context = new SkillCastContext
        {
            Position = position,
            HitTargets = hitTargets,
        };

        context.ImpactIndex = 0;
        ResolveImpactDamage(position);
        ImpactResolved?.Invoke(context);

        // 반복분은 간격을 두고 발동 — "한 번 누르면 잠시 뒤 한 번 더" 느낌.
        // 파괴 토큰과 링크한 소스로 예약해, 파괴뿐 아니라 웨이브 종료(HandleWaveEnd)에서도 취소된다(#200 ②).
        if (context.ExtraImpacts > 0)
        {
            repeatCts ??= CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            RepeatImpactsAsync(context, repeatCts.Token).Forget();
        }

        cooldownTimer = effectiveCooldown;
        return true;
    }

    async UniTaskVoid RepeatImpactsAsync(SkillCastContext context, CancellationToken cancellationToken)
    {
        for (int impact = 1; impact <= context.ExtraImpacts; impact++)
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(Mathf.Max(context.ExtraImpactInterval, 0f)),
                cancellationToken: cancellationToken);

            context.ImpactIndex = impact;
            ResolveImpactDamage(context.Position);
            ImpactResolved?.Invoke(context);
        }
    }

    // 임팩트 1회: 반경 내 적 전체에게 데미지 적용 + 맞은 적을 hitTargets에 수집 + 연출.
    void ResolveImpactDamage(Vector3 position)
    {
        hitTargets.Clear();

        int count = Physics.OverlapSphereNonAlloc(position, effectiveRadius, hitBuffer, enemyLayerMask);
        int damagedCount = 0;
        for (int i = 0; i < count; i++)
        {
            var damageable = hitBuffer[i].GetComponentInParent<IDamageable>();
            // Source: 플레이어 스킬은 IAttacker 개체(타워/몬스터)가 아니라 직접 시전이라 null로 둔다.
            // 현재 DamageInfo.Source는 어디서도 역참조하지 않아 안전(StatusEffectHandler.cs 참고).
            if (damageable != null && damageable.Faction == Faction.Enemy && !damageable.IsDead)
            {
                damageable.TakeDamage(new DamageInfo(effectiveDamage, null));
                damagedCount++;
                if (!damageable.IsDead)   // 즉사한 적은 특수효과 대상에서 제외
                    hitTargets.Add(damageable);
            }
        }

        // 쿨다운이 있어 자주 호출되지 않으므로 로그 스팸 걱정 없이 시전마다 요약을 남긴다(테스트용).
        Debug.Log($"[Skill] 감전 시전: 위치={position}, 적중={damagedCount}마리, 데미지={effectiveDamage}");

        ApplyImpact(position);
    }

    // 테스트 하네스 전용: 검증용으로 소모한 쿨다운을 즉시 리셋해 인터랙티브 테스트를 바로 이어갈 수 있게 한다.
    public void DebugResetCooldown() => cooldownTimer = 0f;


    // 착탄 이펙트: 마법 연구소 레벨에 맞는 프리팹(_currentVisual)을 쓰고, 세트가 없거나
    // 해당 레벨 엔트리가 없으면 기존 impactEffectPrefab으로 폴백한다.
    void ApplyImpact(Vector3 position)
    {
        SkillVisualSet.LevelVisual entry = _currentVisual;
        GameObject prefab = entry != null ? entry.Prefab : impactEffectPrefab;

        if (prefab != null)
        {
            var go = Instantiate(prefab, position, Quaternion.identity);
            // 연구소는 RadiusMultiplier도 올린다. 이펙트 크기만 그대로면 인디케이터와 어긋나 티가 난다
            // (SkillRangeIndicator가 aura를 Radius에 맞춰 스케일하는 것과 같은 맥락).
            if (entry != null && entry.ScaleWithRadius && radius > 0f)
                go.transform.localScale *= effectiveRadius / radius;
        }

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
