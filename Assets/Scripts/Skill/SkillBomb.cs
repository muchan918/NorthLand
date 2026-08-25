using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;
using NorthLand.Core;

// 폭탄(#169)의 폭발체. BombEffect가 감전 착탄 지점에 프리팹으로 생성하고 Init으로 수치를 주입한다.
// 지연 시간이 지나면 반경 내 적 전체에게 즉시 데미지를 주고 소멸한다.
public class SkillBomb : MonoBehaviour
{
    float damage;
    float radius;
    LayerMask enemyLayerMask;
    GameObject explosionEffectPrefab;
    float explosionEffectScale = 1f;
    bool debugLog;

    float timer;
    bool initialized;
    readonly List<IDamageable> hits = new List<IDamageable>(16);

    public void Init(
        float damage,
        float radius,
        float delay,
        LayerMask enemyLayerMask,
        GameObject explosionEffectPrefab,
        float explosionEffectScale,
        bool debugLog)
    {
        this.damage = damage;
        this.radius = radius;
        this.enemyLayerMask = enemyLayerMask;
        this.explosionEffectPrefab = explosionEffectPrefab;
        this.explosionEffectScale = explosionEffectScale;
        this.debugLog = debugLog;
        timer = delay;
        initialized = true;

        // 폭발 전 웨이브 종료(밤→낮) 또는 승리/게임오버(런 종료) 시 폭발 없이 정리한다(#200 ②·리뷰).
        // 매니저가 없으면(예: 테스트 씬) 해당 구독만 스킵하고 폭탄은 그냥 정상 폭발한다.
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnNightToDay += HandleWaveEnd;
        if (GameManager.Instance != null)
            GameManager.Instance.OnResultDecided += HandleResultDecided;
    }

    // 웨이브 종료/런 종료 시: 폭발 없이 소멸(적이 사라졌거나 결과 화면 뒤에서 뒤늦게 터지지 않게).
    // initialized를 내려 파괴 지연(풀링·페이드) 중 Update가 재폭발시키는 것을 막는다.
    void HandleWaveEnd()
    {
        initialized = false;
        Destroy(gameObject);
    }

    void HandleResultDecided(GameResult _) => HandleWaveEnd();

    void OnDestroy()
    {
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnNightToDay -= HandleWaveEnd;
        if (GameManager.Instance != null)
            GameManager.Instance.OnResultDecided -= HandleResultDecided;
    }

    void Update()
    {
        if (!initialized) return;   // Init 없이 씬에 놓였거나 이미 무장 해제된 경우 폭발하지 않음

        timer -= Time.deltaTime;
        if (timer <= 0f)
            Explode();
    }

    void Explode()
    {
        initialized = false;   // 이중 폭발 방어(같은 프레임 재진입 차단)
        SkillHitScan.CollectEnemies(transform.position, radius, enemyLayerMask, hits);

        // Source: 플레이어 스킬 계열은 IAttacker 개체가 아니라 null (SkillManager의 DamageInfo와 동일 규약).
        foreach (var damageable in hits)
            damageable.TakeDamage(new DamageInfo(damage, null));

        if (explosionEffectPrefab != null)
        {
            var effect = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            effect.transform.localScale = explosionEffectPrefab.transform.localScale * explosionEffectScale;
            effect.AddComponent<BombExplosionEffectLifetime>();
        }

        if (debugLog)
            Debug.Log($"[SkillEffect] 폭탄 폭발: 위치={transform.position}, 적중={hits.Count}마리, 데미지={damage}, 반경={radius}");

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

// Imported 폭발 이펙트의 원본 설정을 바꾸지 않고, 모든 자식 파티클이 끝난 뒤 런타임 인스턴스만 정리한다.
sealed class BombExplosionEffectLifetime : MonoBehaviour
{
    ParticleSystem[] particles;
    bool observedAlive;

    void Awake() => particles = GetComponentsInChildren<ParticleSystem>(true);

    void Update()
    {
        if (particles.Length == 0)
        {
            Destroy(gameObject);
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            if (particles[i] != null && particles[i].IsAlive(true))
            {
                observedAlive = true;
                return;
            }
        }

        if (observedAlive)
            Destroy(gameObject);
    }
}
