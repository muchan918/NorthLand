using System;
using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;
using NorthLand.Core;

// 플레이어의 두 번째 스킬(#103) — 버프. 감전(SkillManager)과 달리 위치 타겟팅이 없다: 버튼을 누르면
// 즉시 씬의 모든 Tower에게 공격력/공격속도 배율을 일정 시간 부여한다. 감전과 병렬 구조(밤 게이팅+
// 쿨다운)라 공통 로직이 겹치지만, 스킬이 2개뿐이라 공통 베이스 클래스로 추상화하지 않는다.
// AuraTower(Magic 타입)는 AttackFields 자체가 없어(공격력/공격속도 개념 없음) 버프 대상에서 제외.
public class BuffSkillManager : MonoBehaviour
{
    public static BuffSkillManager Instance { get; private set; }

    [Header("버프 스킬 스탯 (타격감 튜닝용)")]
    [SerializeField] float damageMultiplier = 1.3f;
    [SerializeField] float attackSpeedMultiplier = 1.3f;
    [SerializeField] float buffDuration = 10f;
    [SerializeField] float cooldown = 20f;

    // 마법 연구소(#205) — 레벨 비례로 기본 스탯(공격력·공속 배율/지속시간/쿨다운)을 배율 강화한다.
    // 컨트롤러는 레벨(int)만 노출하고, 레벨→배율 매핑은 `_magicLabAsset.Skill.UpgradeLevels`(SO)에
    // authoring한다 — 비용과 배율이 같은 리스트라 레벨 개수가 어긋날 수 없다(PR#216 리뷰, 씬 리스트 제거).
    // 보상 특수효과(#169, SkillEffect.Level)와는 독립 축 — BuffResolved 구독 흐름은 건드리지 않는다.
    [Header("마법 연구소 강화 (#205)")]
    [Tooltip("비우면 강화 없음(레벨 0 취급)")]
    [SerializeField] BuildingAsset _magicLabAsset;
    [Tooltip("비우면 씬에서 자동 탐색")]
    [SerializeField] ManagementController _managementController;

    // 버프 시전이 끝날 때마다 발행 — 보상으로 획득한 버프 계열 특수효과(SkillEffect 파생,
    // 예: BurnBuff)가 구독한다. 구독자가 없으면 기본 버프 그대로. (감전의 ImpactResolved와 동일 구조)
    public event Action<BuffCastContext> BuffResolved;

    float cooldownTimer;

    // 합산 중첩(#164)용 소스키 — 버프 타워의 소스키(TowerID 해시)와 겹치지 않는 고정 식별자.
    static readonly int SkillSourceId = "skill.player_buff".GetHashCode();

    // 마법 연구소 레벨로 계산한 유효 스탯(레벨 0/미배선이면 기본값과 동일).
    float effectiveDamageMultiplier;
    float effectiveAttackSpeedMultiplier;
    float effectiveDuration;
    float effectiveCooldown;
    int lastMagicLabLevel = -1; // 레벨 변경 시에만 로그를 남기기 위한 캐시(-1: 최초 1회는 무조건 로그).

    public bool IsReady => cooldownTimer <= 0f;
    public float CooldownRemaining01 => effectiveCooldown <= 0f ? 0f : Mathf.Clamp01(cooldownTimer / effectiveCooldown);

    void Awake()
    {
        if(WarnRetired()) return;

        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // #315로 은퇴한 스킬(GDD §5.5)이 씬에 되살아났을 때 콘솔에 드러낸다.
    // 씬 정본 확정은 스냅샷 덮어쓰기라 삭제가 병합으로 보존되지 않는데(SceneWorkflow.md §4),
    // 스크립트를 남긴 결정 때문에 부활해도 경고가 0건이라 무증상이다 — 그 침묵을 깨는 것이 목적이다.
    // 되살릴 때는 이 메서드와 Awake 첫 줄만 지운다.
    bool WarnRetired()
    {
        Debug.LogError("[#315] 제거된 버프 스킬(BuffSkillManager)이 씬에 배선돼 있습니다 — 이 오브젝트를 삭제하세요.", this);
        enabled = false;
        return true;
    }

    void Start()
    {
        // 마법 연구소(#205) — 비워두면 씬에서 자동 탐색(BuildingInfoUI와 동일 관례).
        if (_managementController == null)
            _managementController = FindFirstObjectByType<ManagementController>();
        if (_managementController != null)
            _managementController.OnChanged += RefreshUpgrade;
        RefreshUpgrade();
    }

    void OnDestroy()
    {
        if (_managementController != null)
            _managementController.OnChanged -= RefreshUpgrade;
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
            effectiveDamageMultiplier = damageMultiplier * PositiveOr1(scaling.BuffDamageMultiplierScale);
            effectiveAttackSpeedMultiplier = attackSpeedMultiplier * PositiveOr1(scaling.BuffAttackSpeedMultiplierScale);
            effectiveDuration = buffDuration * PositiveOr1(scaling.BuffDurationMultiplier);
            effectiveCooldown = cooldown * PositiveOr1(scaling.BuffCooldownMultiplier);
        }
        else
        {
            effectiveDamageMultiplier = damageMultiplier;
            effectiveAttackSpeedMultiplier = attackSpeedMultiplier;
            effectiveDuration = buffDuration;
            effectiveCooldown = cooldown;
        }

        if (level != lastMagicLabLevel)
        {
            Debug.Log($"[BuffSkill] 마법 연구소 Lv{level} 적용 — 데미지배율={effectiveDamageMultiplier:F2}, 공속배율={effectiveAttackSpeedMultiplier:F2}, 지속시간={effectiveDuration}초, 쿨다운={effectiveCooldown}초");
            lastMagicLabLevel = level;
        }
    }

    // 인스펙터에서 리스트에 새 레벨 엔트리를 추가하면 필드 기본값이 1이라 이 헬퍼는 대부분 no-op이지만,
    // 과거 데이터나 실수로 0/음수가 들어와도 1.0(배율 없음)으로 취급해 방어한다(PR#216 리뷰) — 쿨다운
    // 0=무한 연발 같은 조용한 파괴적 결과를 막는다.
    static float PositiveOr1(float multiplier) => multiplier > 0f ? multiplier : 1f;

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

    // 타겟팅 없이 즉시 발동 — 씬의 모든 Tower(Tower.Active, FindObjectsByType 대신 자가 등록 리스트)에 버프 적용.
    public bool Activate()
    {
        if (!CanCast()) return false;

        foreach (var tower in Tower.Active)
            tower.ApplyBuff(SkillSourceId, effectiveDamageMultiplier, effectiveAttackSpeedMultiplier, effectiveDuration);

        Debug.Log($"[BuffSkill] 발동: 타워 {Tower.Active.Count}개, 데미지x{effectiveDamageMultiplier}, 공속x{effectiveAttackSpeedMultiplier}, {effectiveDuration}초");

        BuffResolved?.Invoke(new BuffCastContext { Duration = effectiveDuration });

        cooldownTimer = effectiveCooldown;
        return true;
    }

    // 테스트 하네스 전용: 검증용으로 소모한 쿨다운을 즉시 리셋.
    public void DebugResetCooldown() => cooldownTimer = 0f;
}
