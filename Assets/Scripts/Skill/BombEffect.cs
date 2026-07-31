using UnityEngine;

// 폭탄(#169): 감전 임팩트 지점에 폭탄(구 프리팹)을 설치하고, 몇 초 뒤 범위 폭발 데미지.
// 레벨업 = 폭발 데미지 증가. 실제 폭발은 생성된 SkillBomb이 수행한다.
public class BombEffect : SkillEffect
{
    [Header("폭탄 수치 (폭발 데미지 = 레벨 x 레벨당 데미지)")]
    [SerializeField] GameObject bombPrefab;
    [SerializeField] float damagePerLevel = 20f;
    [SerializeField] float explosionDelay = 2f;
    [SerializeField] float explosionRadius = 3f;

    // TODO(TBD): SkillManager와 동일하게 임시 LayerMask 방식. 팀 컨벤션 확정 후 정리(Tower.cs 참고).
    [SerializeField] LayerMask enemyLayerMask;

    [Header("디버그")]
    [SerializeField] bool debugLog;   // 폭탄 설치/폭발을 Console에 출력 (검증용)

    public override WaveRewardType Type => WaveRewardType.Bomb;

    protected override void HandleImpact(SkillCastContext context)
    {
        if (bombPrefab == null)
        {
            Debug.LogWarning("[SkillEffect] BombEffect에 bombPrefab이 연결되지 않아 폭탄을 설치할 수 없음", this);
            return;
        }

        var bombObject = Instantiate(bombPrefab, context.Position, Quaternion.identity);

        var bomb = bombObject.GetComponent<SkillBomb>();
        if (bomb == null) bomb = bombObject.AddComponent<SkillBomb>();

        bomb.Init(damagePerLevel * Level, explosionRadius, explosionDelay, enemyLayerMask, debugLog);

        if (debugLog)
            Debug.Log($"[SkillEffect] 폭탄 설치: 위치={context.Position}, Lv{Level}, {explosionDelay}초 뒤 폭발");
    }
}
