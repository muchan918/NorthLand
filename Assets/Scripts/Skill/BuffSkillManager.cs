using System;
using UnityEngine;
using NorthLand.Combat;

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

    // 버프 시전이 끝날 때마다 발행 — 보상으로 획득한 버프 계열 특수효과(SkillEffect 파생,
    // 예: BurnBuff)가 구독한다. 구독자가 없으면 기본 버프 그대로. (감전의 ImpactResolved와 동일 구조)
    public event Action<BuffCastContext> BuffResolved;

    float cooldownTimer;

    // 합산 중첩(#164)용 소스키 — 버프 타워의 소스키(TowerID 해시)와 겹치지 않는 고정 식별자.
    static readonly int SkillSourceId = "skill.player_buff".GetHashCode();

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

    // 타겟팅 없이 즉시 발동 — 씬의 모든 Tower(Tower.Active, FindObjectsByType 대신 자가 등록 리스트)에 버프 적용.
    public bool Activate()
    {
        if (!CanCast()) return false;

        foreach (var tower in Tower.Active)
            tower.ApplyBuff(SkillSourceId, damageMultiplier, attackSpeedMultiplier, buffDuration);

        Debug.Log($"[BuffSkill] 발동: 타워 {Tower.Active.Count}개, 데미지x{damageMultiplier}, 공속x{attackSpeedMultiplier}, {buffDuration}초");

        BuffResolved?.Invoke(new BuffCastContext { Duration = buffDuration });

        cooldownTimer = cooldown;
        return true;
    }

    // 테스트 하네스 전용: 검증용으로 소모한 쿨다운을 즉시 리셋.
    public void DebugResetCooldown() => cooldownTimer = 0f;
}
