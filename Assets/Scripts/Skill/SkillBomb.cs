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
    readonly Collider[] hitBuffer = new Collider[16];

    public void Init(float damage, float radius, float delay, LayerMask enemyLayerMask, bool debugLog)
    {
        this.damage = damage;
        this.radius = radius;
        this.enemyLayerMask = enemyLayerMask;
        this.debugLog = debugLog;
        timer = delay;
        initialized = true;
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
