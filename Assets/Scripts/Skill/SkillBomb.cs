using UnityEngine;
using NorthLand.Combat;

// 폭탄(#169)의 폭발체. BombEffect가 감전 착탄 지점에 프리팹으로 생성하고 Init으로 수치를 주입한다.
// 지연 시간이 지나면 반경 내 적 전체에게 즉시 데미지를 주고 소멸한다.
public class SkillBomb : MonoBehaviour
{
    float damage;
    float radius;
    LayerMask enemyLayerMask;
    bool debugLog;

    float timer;
    bool initialized;
    bool subscribedWaveEnd;   // OnNightToDay 구독 여부(중복 해제/누수 방지)
    readonly Collider[] hitBuffer = new Collider[16];

    public void Init(float damage, float radius, float delay, LayerMask enemyLayerMask, bool debugLog)
    {
        this.damage = damage;
        this.radius = radius;
        this.enemyLayerMask = enemyLayerMask;
        this.debugLog = debugLog;
        timer = delay;
        initialized = true;

        // 웨이브 종료(밤→낮) 시 폭발 전이면 폭발 없이 정리한다(#200 ②). DayNightManager가 없으면
        // (예: 테스트 씬) 구독을 스킵하고 폭탄은 그냥 정상 폭발한다.
        if (DayNightManager.Instance != null)
        {
            DayNightManager.Instance.OnNightToDay += HandleWaveEnd;
            subscribedWaveEnd = true;
        }
    }

    // 웨이브가 끝났으면 폭발 없이 소멸(적이 이미 사라진 낮에 뒤늦게 터지지 않게).
    void HandleWaveEnd() => Destroy(gameObject);

    void OnDestroy()
    {
        if (subscribedWaveEnd && DayNightManager.Instance != null)
            DayNightManager.Instance.OnNightToDay -= HandleWaveEnd;
    }

    void Update()
    {
        if (!initialized) return;   // Init 없이 씬에 놓인 경우 폭발하지 않음

        timer -= Time.deltaTime;
        if (timer <= 0f)
            Explode();
    }

    void Explode()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, hitBuffer, enemyLayerMask);
        int hitTargets = 0;
        for (int i = 0; i < count; i++)
        {
            var damageable = hitBuffer[i].GetComponentInParent<IDamageable>();
            // Source: 플레이어 스킬 계열은 IAttacker 개체가 아니라 null (SkillManager의 DamageInfo와 동일 규약).
            if (damageable != null && damageable.Faction == Faction.Enemy && !damageable.IsDead)
            {
                damageable.TakeDamage(new DamageInfo(damage, null));
                hitTargets++;
            }
        }

        if (debugLog)
            Debug.Log($"[SkillEffect] 폭탄 폭발: 위치={transform.position}, 적중={hitTargets}마리, 데미지={damage}, 반경={radius}");

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
