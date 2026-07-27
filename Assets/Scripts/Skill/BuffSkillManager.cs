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
    // 컨트롤러는 레벨(int)만 노출하고, 레벨→배율 매핑은 이 클래스(소비 측)가 소유한다(BuildingUpgrade.md §8).
    // 보상 특수효과(#169, SkillEffect.Level)와는 독립 축 — BuffResolved 구독 흐름은 건드리지 않는다.
    [Header("마법 연구소 강화 (#205, 수치는 placeholder)")]
    [Tooltip("비우면 강화 없음(레벨 0 취급)")]
    [SerializeField] BuildingAsset _magicLabAsset;
    [Tooltip("비우면 씬에서 자동 탐색")]
    [SerializeField] ManagementController _managementController;
    [SerializeField] List<BuffUpgradeLevel> _upgradeLevels;

    [Serializable]
    struct BuffUpgradeLevel
    {
        public float damageMultiplierScale;
        public float attackSpeedMultiplierScale;
        public float durationMultiplier;
        public float cooldownMultiplier;
    }

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

        if (_upgradeLevels != null && level > 0 && level <= _upgradeLevels.Count)
        {
            BuffUpgradeLevel scaling = _upgradeLevels[level - 1];
            effectiveDamageMultiplier = damageMultiplier * scaling.damageMultiplierScale;
            effectiveAttackSpeedMultiplier = attackSpeedMultiplier * scaling.attackSpeedMultiplierScale;
            effectiveDuration = buffDuration * scaling.durationMultiplier;
            effectiveCooldown = cooldown * scaling.cooldownMultiplier;
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
