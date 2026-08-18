using System;
using System.Collections.Generic;
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
    [SerializeField] float cooldown = 10f;

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

    // 충전(탄약) 방식(#319). 시계는 이 하나뿐이며, 만충이 아닐 때 effectiveCooldown 간격으로
    // 1발씩 채운다 — 롤 티모 버섯과 같은 규칙이다. 쿨다운 타이머를 따로 두지 않는 이유:
    // 시계가 둘이면 최대 충전과 무관하게 회복 속도가 두 배가 되어 "간격당 1발"이 깨진다.
    int charges;
    float rechargeTimer;
    int bonusCharges;   // 추가시전(#319) 보상이 올려주는 추가 최대 충전

    readonly List<IDamageable> hitTargets = new List<IDamageable>(16);
    // RemoveAll에 매번 람다를 넘기면 호출마다 델리게이트가 할당된다. 시전마다 도는 경로라 캐시한다.
    static readonly Predicate<IDamageable> s_IsDead = d => d.IsDead;

    // 마법 연구소 레벨로 계산한 유효 스탯(레벨 0/미배선이면 기본값과 동일).
    float effectiveDamage;
    float effectiveRadius;
    float effectiveCooldown;
    int lastMagicLabLevel = -1; // 레벨 변경 시에만 로그를 남기기 위한 캐시(-1: 최초 1회는 무조건 로그).

    // 레벨별 착탄 이펙트. 레벨이 바뀔 때만 조회하면 되므로 RefreshUpgrade에서 캐싱한다
    // (effectiveDamage 등을 미리 계산해 두는 것과 같은 이유).
    SkillVisualSet.LevelVisual _currentVisual;

    public float Radius => effectiveRadius;

    // 보상이 없어도 들고 있는 기본 충전. 보상 카드가 "충전 횟수 N → N+1"을 계산할 때도 이 값을
    // 써야 버튼 표기("2/2")와 갈리지 않는다 — 두 화면이 각자 `1 +`을 들고 있으면 기본값을 바꾸는
    // 순간 조용히 어긋난다.
    public const int BaseCharges = 1;

    public int Charges => charges;
    public int MaxCharges => MaxChargesWith(bonusCharges);

    /// 보너스 충전이 <paramref name="bonus"/>일 때의 최대 충전. 아직 획득하지 않은 레벨의 값을
    /// 미리 보여줘야 하는 보상 카드가 쓴다.
    public static int MaxChargesWith(int bonus) => BaseCharges + Mathf.Max(0, bonus);

    public bool IsReady => charges > 0;

    // 다음 1발이 찰 때까지 남은 시간(초). 만충이면 0 — 버튼이 카운트다운을 숨기는 신호로 쓴다.
    public float RechargeRemaining =>
        charges >= MaxCharges ? 0f : Mathf.Max(effectiveCooldown - rechargeTimer, 0f);

    // 추가시전(#319) 보상이 레벨을 밀어넣는다. 최대가 줄어드는 경우(0레벨 복원 등) 보유분도
    // 같이 깎아 최대를 넘는 상태가 남지 않게 한다.
    public void SetBonusCharges(int value)
    {
        bonusCharges = Mathf.Max(0, value);
        charges = Mathf.Min(charges, MaxCharges);
    }

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
        // 밤 시작마다 만충으로 되돌린다 — 낮 길이와 무관하게 매 웨이브 같은 조건에서 출발시킨다.
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnDayToNight += RefillCharges;
        else
            Debug.LogWarning("[Skill] DayNightManager를 찾을 수 없어 밤 시작 시 충전이 리셋되지 않습니다.");

        // 마법 연구소(#205) — 비워두면 씬에서 자동 탐색(BuildingInfoUI와 동일 관례).
        if (_managementController == null)
            _managementController = FindFirstObjectByType<ManagementController>();
        if (_managementController != null)
            _managementController.OnChanged += RefreshUpgrade;
        RefreshUpgrade();

        RefillCharges();
    }

    void OnDestroy()
    {
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnDayToNight -= RefillCharges;
        if (_managementController != null)
            _managementController.OnChanged -= RefreshUpgrade;
    }

    void RefillCharges()
    {
        charges = MaxCharges;
        rechargeTimer = 0f;
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
            Debug.Log($"[Skill] 마법 연구소 Lv{level} 적용 — 감전 데미지={effectiveDamage}(x{effectiveDamage / damage:F2}), 사거리={effectiveRadius}(x{effectiveRadius / radius:F2}), 재충전 간격={effectiveCooldown}(x{effectiveCooldown / cooldown:F2})");
            lastMagicLabLevel = level;
        }
    }

    // BuildingAsset.SkillUpgradeLevel의 배율 필드 기본값이 1이라 이 헬퍼는 대부분 no-op이지만, 과거
    // 데이터나 실수로 0/음수가 들어와도 1.0(배율 없음)으로 취급해 방어한다(PR#216 리뷰) — 쿨다운 0=무한
    // 연발, 사거리 0=미적중 같은 조용한 파괴적 결과를 막는다.
    static float PositiveOr1(float multiplier) => multiplier > 0f ? multiplier : 1f;

    // 만충이 아닐 때만 시계가 돌고, 간격마다 1발씩 채운다. 만충에서 타이머를 0으로 되돌리므로
    // 다음 시전 직후의 재충전은 항상 온전한 간격에서 출발한다.
    void Update()
    {
        if (charges >= MaxCharges)
        {
            rechargeTimer = 0f;
            return;
        }

        rechargeTimer += Time.deltaTime;
        if (rechargeTimer >= effectiveCooldown)
        {
            rechargeTimer -= effectiveCooldown;
            charges = Mathf.Min(charges + 1, MaxCharges);
        }
    }

    public bool CanCast()
    {
        if (GameManager.Instance != null && GameManager.Instance.Result != GameResult.Playing) return false;
        if (!IsReady) return false;
        if (DayNightManager.Instance != null &&
            DayNightManager.Instance.CurrentPhase != DayNightManager.Phase.Night) return false;
        return true;
    }

    // 클릭한 위치를 중심으로 감전 임팩트를 발동한다. 충전 1발을 소모하며, 남은 충전이 있으면
    // 대기 없이 곧바로 다시 시전할 수 있다(#319).
    public bool CastAt(Vector3 position)
    {
        if (!CanCast()) return false;

        charges--;

        var context = new SkillCastContext
        {
            Position = position,
            HitTargets = hitTargets,
        };

        ResolveImpactDamage(position);
        ImpactResolved?.Invoke(context);

        return true;
    }

    // 임팩트 1회: 반경 내 적 전체에게 데미지 적용 + 맞은 적을 hitTargets에 수집 + 연출.
    void ResolveImpactDamage(Vector3 position)
    {
        SkillHitScan.CollectEnemies(position, effectiveRadius, enemyLayerMask, hitTargets);
        int damagedCount = hitTargets.Count;

        // Source: 플레이어 스킬은 IAttacker 개체(타워/몬스터)가 아니라 직접 시전이라 null로 둔다.
        // 현재 DamageInfo.Source는 어디서도 역참조하지 않아 안전(StatusEffectHandler.cs 참고).
        foreach (var damageable in hitTargets)
            damageable.TakeDamage(new DamageInfo(effectiveDamage, null));

        hitTargets.RemoveAll(s_IsDead);   // 즉사한 적은 특수효과 대상에서 제외

        // 쿨다운이 있어 자주 호출되지 않으므로 로그 스팸 걱정 없이 시전마다 요약을 남긴다(테스트용).
        Debug.Log($"[Skill] 감전 시전: 위치={position}, 적중={damagedCount}마리, 데미지={effectiveDamage}");

        ApplyImpact(position);
    }

    // 테스트 하네스가 검증으로 소모한 충전을 되돌려 인터랙티브 테스트를 바로 이어갈 때 쓴다.
    public void RefillChargesNow() => RefillCharges();

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
