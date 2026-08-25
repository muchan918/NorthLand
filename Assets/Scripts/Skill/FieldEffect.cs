using UnityEngine;

// 전기장(#316): 감전 임팩트 지점에 지속 장판을 설치하고, tickInterval마다 그 순간 범위 안의 적에게 데미지.
// 레벨업 = 틱 데미지 증가. 실제 판정은 생성된 SkillField가 수행한다.
//
// 화상(BurnEffect)과의 차이: 화상은 맞은 "대상"에 DoT를 붙여 따라다니고, 전기장은 "위치"에 결속된다.
// 그래서 여기서는 context.HitTargets를 쓰지 않고 context.Position만 읽는다.
public class FieldEffect : SkillEffect
{
    [Header("전기장 수치 (틱 데미지 = 레벨 x 레벨당 틱 데미지)")]
    [SerializeField] GameObject fieldPrefab;
    [SerializeField] float tickDamagePerLevel = 3f;
    [SerializeField] float tickInterval = 0.5f;
    [SerializeField] float duration = 3f;
    [SerializeField] bool loopParticlesForDuration;
    [SerializeField] ParticleSystem[] fieldParticles;

    // 감전 반경(SkillManager 기본 6)의 60%를 의도한 값이지만 종속시키지 않고 독립 수치로 둔다 —
    // 싱글톤 의존 없이 BombEffect.explosionRadius와 같은 패턴을 유지하고,
    // 마법 연구소의 RadiusMultiplier가 장판까지 증폭시키는 의도치 않은 결합을 만들지 않는다.
    [Tooltip("Field Prefab 루트에 CapsuleCollider가 없을 때만 사용하는 예비 반경")]
    [SerializeField] float fieldRadius = 3.6f;

    // TODO(TBD): SkillManager와 동일하게 임시 LayerMask 방식. 팀 컨벤션 확정 후 정리(Tower.cs 참고).
    [SerializeField] LayerMask enemyLayerMask;

    [Header("디버그")]
    [SerializeField] bool debugLog;   // 장판 설치/틱 판정을 Console에 출력 (검증용)

    public override WaveRewardType Type => WaveRewardType.Field;

    // 보상 패널(#287) 표시용. HandleImpact의 실제 계산과 같은 식(미보유 = Lv0 = 0).
    // 반경은 레벨과 무관한 고정값이라 그대로 노출한다.
    public float GetCurrentTickDamage() => GetTickDamageAt(Level);
    public float GetTickDamageAt(int level) => tickDamagePerLevel * level;
    public float FieldRadius => GetAuthoredFieldRadius();

    public override string GetStatSummary()
        => SkillStatsFormatter.BuildFieldLines(GetCurrentTickDamage(), GetTickDamageAt(NextLevel), FieldRadius);

    protected override void HandleImpact(SkillCastContext context)
    {
        if (fieldPrefab == null)
        {
            Debug.LogWarning("[SkillEffect] FieldEffect에 fieldPrefab이 연결되지 않아 장판을 설치할 수 없음", this);
            return;
        }

        // HitTargets를 읽지 않는다 — 장판은 착탄 시점 피격자가 아니라 위치에 결속된다.
        var fieldObject = Instantiate(fieldPrefab, context.Position, Quaternion.identity);

        // Imported 이펙트는 기본적으로 비반복 재생이라 장판 판정보다 먼저 사라진다.
        // 테스트 씬에서는 장판 수명 동안 반복시켜 시각 효과와 실제 판정 시간을 맞춘다.
        if (loopParticlesForDuration)
        {
            foreach (var sourceParticle in fieldParticles)
            {
                var particle = FindInstantiatedParticle(sourceParticle, fieldObject.transform);
                if (particle == null) continue;

                var main = particle.main;
                main.loop = true;
            }
        }

        var field = fieldObject.GetComponent<SkillField>();
        if (field == null) field = fieldObject.AddComponent<SkillField>();

        float effectiveFieldRadius = GetAuthoredFieldRadius();

        field.Init(
            tickDamagePerLevel * Level,
            effectiveFieldRadius,
            duration,
            tickInterval,
            enemyLayerMask,
            debugLog);

        if (debugLog)
            Debug.Log($"[SkillEffect] 전기장 설치: 위치={context.Position}, Lv{Level}, 틱당 {tickDamagePerLevel * Level} / {tickInterval}s, 지속 {duration}s, 반경 {effectiveFieldRadius}");
    }

    float GetAuthoredFieldRadius()
    {
        if (fieldPrefab == null) return fieldRadius;

        CapsuleCollider capsule = fieldPrefab.GetComponent<CapsuleCollider>();
        if (capsule == null) return fieldRadius;

        Vector3 scale = capsule.transform.lossyScale;
        float radiusScale = capsule.direction switch
        {
            0 => Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)),
            2 => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)),
            _ => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)),
        };
        return capsule.radius * radiusScale;
    }

    ParticleSystem FindInstantiatedParticle(ParticleSystem sourceParticle, Transform instanceRoot)
    {
        if (sourceParticle == null || fieldPrefab == null) return null;

        Transform sourceRoot = fieldPrefab.transform;
        Transform sourceTransform = sourceParticle.transform;
        if (sourceTransform == sourceRoot)
            return instanceRoot.GetComponent<ParticleSystem>();

        var path = new System.Collections.Generic.Stack<string>();
        while (sourceTransform != null && sourceTransform != sourceRoot)
        {
            path.Push(sourceTransform.name);
            sourceTransform = sourceTransform.parent;
        }

        if (sourceTransform != sourceRoot) return null;

        Transform instanceTransform = instanceRoot.Find(string.Join("/", path));
        return instanceTransform != null ? instanceTransform.GetComponent<ParticleSystem>() : null;
    }
}
