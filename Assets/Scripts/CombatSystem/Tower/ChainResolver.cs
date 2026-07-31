using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 체인(연쇄) 명중을 해결한다 — 최초 대상에서 인근 적으로 튕기며 홉마다 데미지를 감쇠시킨다.
    //
    // **명중 축**이라 전달 방식(투사체/히트스캔)을 모른다. 투사체가 착탄했을 때와 히트스캔이 즉시
    // 판정할 때가 같은 규칙을 써야 하므로, 어느 한쪽 안에 들어 있으면 재사용이 불가능하다
    // (AuraModifiers가 적용부·표시부에 공유되는 것과 같은 이유 — WL-130).
    //
    // outPath로 홉 좌표를 돌려주는 이유: 빔 연출이 "누가 맞았는지"를 그려야 하는데, 표시부가 경로를
    // 다시 계산하면 "선은 3명을 잇는데 데미지는 4명" 같은 비대칭이 생긴다.
    public static class ChainResolver
    {
        // 중복 타격 방지 집합 + 대상 수집 버퍼. Resolve가 동기적으로 완료되므로 재진입이 없어
        // static 재사용이 안전하다(여러 타워가 같은 프레임에 발사해도 순차 실행된다).
        static readonly HashSet<IDamageable> hitSet = new HashSet<IDamageable>();

        // NonAlloc은 버퍼 크기를 넘는 결과를 조용히 버린다. 체인 반경은 좁아 32면 충분하다(WL-025).
        static readonly Collider[] hitBuffer = new Collider[32];

        /// 최초 대상부터 최대 maxTargets명까지 튕기며 데미지를 적용한다.
        /// outPath(null 허용)에는 실제로 피해를 입은 대상의 좌표가 타격 순서대로 담긴다.
        public static void Resolve(
            IDamageable firstTarget,
            float damage,
            IAttacker source,
            float chainRadius,
            int maxTargets,
            float damageFalloff,
            LayerMask enemyMask,
            List<Vector3> outPath = null)
        {
            if (firstTarget == null || firstTarget.IsDead) return;

            // 최초 대상은 maxTargets 값과 무관하게 항상 맞는다(미저작 SO에서 0이 들어와도 1발은 성립).
            int limit = Mathf.Max(maxTargets, 1);

            hitSet.Clear();
            outPath?.Clear();

            IDamageable current = firstTarget;
            float dmg = damage;

            for (int hop = 0; hop < limit; hop++)
            {
                // 좌표를 데미지보다 먼저 담는다 — 풀링 도입 후 사망 시 즉시 비활성화되는 대상에서도
                // HitPosition이 유효한 시점에 읽어두기 위함.
                Vector3 pos = current.HitPosition.position;
                outPath?.Add(pos);

                current.TakeDamage(new DamageInfo(dmg, source));
                Projectile.RaiseDamageDealt(source, current);
                hitSet.Add(current);

                if (hop + 1 >= limit) break;

                IDamageable next = FindNearestUnhit(pos, chainRadius, enemyMask, source.Faction);
                if (next == null) break;

                current = next;
                dmg *= damageFalloff;   // 튕길 때마다 ×falloff 누적
            }
        }

        // center 반경 내, 아직 안 맞은 가장 가까운 적
        static IDamageable FindNearestUnhit(Vector3 center, float radius, LayerMask mask, Faction sourceFaction)
        {
            int count = Physics.OverlapSphereNonAlloc(center, radius, hitBuffer, mask);

            IDamageable closest = null;
            float closestSqr = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                IDamageable d = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (d == null || d.Faction == sourceFaction || d.IsDead) continue;
                if (hitSet.Contains(d)) continue;

                float sqr = (d.HitPosition.position - center).sqrMagnitude;
                if (sqr < closestSqr)
                {
                    closestSqr = sqr;
                    closest = d;
                }
            }
            return closest;
        }
    }
}