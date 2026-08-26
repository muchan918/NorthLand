using UnityEngine;

// 폭탄(#169): 감전 임팩트 지점에 폭탄(구 프리팹)을 설치하고, 몇 초 뒤 범위 폭발 데미지.
// 레벨업 = 폭발 데미지 증가. 실제 폭발은 생성된 SkillBomb이 수행한다.
public class BombEffect : SkillEffect
{
    [Header("폭탄 수치 (폭발 데미지 = 레벨 x 레벨당 데미지)")]
    [SerializeField] GameObject bombPrefab;
    [SerializeField] GameObject explosionEffectPrefab;
    [SerializeField, Min(0.01f)] float bombScale = 1f;
    [SerializeField] float damagePerLevel = 20f;
    [SerializeField] float explosionDelay = 2f;
    [SerializeField] float explosionRadius = 3f;

    // TODO(TBD): SkillManager와 동일하게 임시 LayerMask 방식. 팀 컨벤션 확정 후 정리(Tower.cs 참고).
    [SerializeField] LayerMask enemyLayerMask;

    [Header("디버그")]
    [SerializeField] bool debugLog;   // 폭탄 설치/폭발을 Console에 출력 (검증용)

    public override WaveRewardType Type => WaveRewardType.Bomb;

    // 보상 패널(#287) 표시용. HandleImpact의 실제 계산과 같은 식(미보유 = Lv0 = 0).
    // 반경은 레벨과 무관한 고정값이라 그대로 노출한다.
    public float GetCurrentDamage() => GetDamageAt(Level);
    public float GetDamageAt(int level) => damagePerLevel * level;
    public float ExplosionRadius => explosionRadius;

    public override string GetStatSummary()
        => SkillStatsFormatter.BuildBombLines(GetCurrentDamage(), GetDamageAt(NextLevel), ExplosionRadius);

    protected override void HandleImpact(SkillCastContext context)
    {
        if (bombPrefab == null)
        {
            Debug.LogWarning("[SkillEffect] BombEffect에 bombPrefab이 연결되지 않아 폭탄을 설치할 수 없음", this);
            return;
        }

        var bombObject = Instantiate(bombPrefab, context.Position, Quaternion.identity);
        bombObject.transform.localScale = bombPrefab.transform.localScale * bombScale;

        var bomb = bombObject.GetComponent<SkillBomb>();
        if (bomb == null) bomb = bombObject.AddComponent<SkillBomb>();

        bomb.Init(
            damagePerLevel * Level,
            explosionRadius,
            explosionDelay,
            enemyLayerMask,
            explosionEffectPrefab,
            bombScale,
            debugLog);

        if (debugLog)
            Debug.Log($"[SkillEffect] 폭탄 설치: 위치={context.Position}, Lv{Level}, {explosionDelay}초 뒤 폭발");
    }
}
