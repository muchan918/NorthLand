using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 투사체 명중 시 동작 종류
    public enum ImpactKind { Single, Area, Chain }

    // 타워가 발사 시 넘기는 "명중하면 어떻게 터질지" 기술자
    public struct ProjectileImpact
    {
        public ImpactKind Kind;
        public LayerMask EnemyMask;
        public float SplashRadius;        // Area
        public float ChainRadius;         // Chain
        public int MaxChainTargets;     // Chain: 최초 대상 포함 총 타격 수
        public float ChainDamageFalloff;  // Chain: 홉마다 곱해지는 계수(예 0.8)

        public static ProjectileImpact MakeSingle()
            => new ProjectileImpact { Kind = ImpactKind.Single };

        public static ProjectileImpact MakeArea(float splashRadius, LayerMask mask)
            => new ProjectileImpact { Kind = ImpactKind.Area, SplashRadius = splashRadius, EnemyMask = mask };

        public static ProjectileImpact MakeChain(float radius, int maxTargets, float falloff, LayerMask mask)
            => new ProjectileImpact
            {
                Kind = ImpactKind.Chain,
                ChainRadius = radius,
                MaxChainTargets = maxTargets,
                ChainDamageFalloff = falloff,
                EnemyMask = mask
            };
    }

    public class Projectile : MonoBehaviour
    {
        [SerializeField] Vector3 rotationOffset;

        IDamageable target;
        float damage;
        float speed;
        IAttacker source;
        ProjectileImpact impact;

        // 체인 중복 타격 방지용 (한 프레임에 하나의 투사체만 명중 처리되므로 static 재사용 OK)
        static readonly HashSet<IDamageable> chainHitSet = new HashSet<IDamageable>();

        public void Init(IDamageable target, float damage, float speed, IAttacker source, ProjectileImpact impact)
        {
            this.target = target;
            this.damage = damage;
            this.speed = speed;
            this.source = source;
            this.impact = impact;
        }

        void Update()
        {
            var targetObj = target as MonoBehaviour;
            if (targetObj == null || target.IsDead)
            {
                Destroy(gameObject);   // 대상이 도중에 사라지면 소멸
                return;
            }

            Vector3 targetPos = targetObj.transform.position;

            Vector3 dir = targetPos - transform.position;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(rotationOffset);

            transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);

            if (Vector3.Distance(transform.position, targetPos) < 0.1f)
            {
                OnHit(targetPos);
                Destroy(gameObject);
            }
        }

        void OnHit(Vector3 impactPos)
        {
            switch (impact.Kind)
            {
                case ImpactKind.Area: ApplyArea(impactPos); break;
                case ImpactKind.Chain: ApplyChain(); break;
                default: target.TakeDamage(new DamageInfo(damage, source)); break;
            }
        }

        // 명중 지점 반경 내 모든 적에게 동일 데미지
        void ApplyArea(Vector3 impactPos)
        {
            var hits = Physics.OverlapSphere(impactPos, impact.SplashRadius, impact.EnemyMask);
            foreach (var h in hits)
            {
                var d = h.GetComponentInParent<IDamageable>();
                if (d != null && d.Faction != source.Faction && !d.IsDead)
                    d.TakeDamage(new DamageInfo(damage, source));
            }
        }

        // 대상 → 인근 적으로 튕기며 홉마다 데미지 *= falloff
        void ApplyChain()
        {
            chainHitSet.Clear();

            float dmg = damage;
            target.TakeDamage(new DamageInfo(dmg, source));   // 최초 대상: 풀 데미지
            chainHitSet.Add(target);

            Vector3 from = (target as MonoBehaviour).transform.position;

            for (int i = 1; i < impact.MaxChainTargets; i++)
            {
                var next = FindNearestUnhit(from, impact.ChainRadius);
                if (next == null) break;

                dmg *= impact.ChainDamageFalloff;             // 튕길 때마다 ×0.8 누적
                next.TakeDamage(new DamageInfo(dmg, source));
                chainHitSet.Add(next);
                from = (next as MonoBehaviour).transform.position;
            }
        }

        // center 반경 내, 아직 안 맞은 가장 가까운 적
        IDamageable FindNearestUnhit(Vector3 center, float radius)
        {
            var hits = Physics.OverlapSphere(center, radius, impact.EnemyMask);
            IDamageable closest = null;
            float closestSqr = float.MaxValue;
            foreach (var h in hits)
            {
                var d = h.GetComponentInParent<IDamageable>();
                if (d == null || d.Faction == source.Faction || d.IsDead) continue;
                if (chainHitSet.Contains(d)) continue;
                float sqr = (h.transform.position - center).sqrMagnitude;
                if (sqr < closestSqr) { closestSqr = sqr; closest = d; }
            }
            return closest;
        }
    }
}