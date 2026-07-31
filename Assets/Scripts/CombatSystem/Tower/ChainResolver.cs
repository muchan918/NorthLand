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
    //
    // ── 상태를 static이 아니라 인스턴스로 두는 이유 ─────────────────────────────
    // 순회 도중 외부 코드로 나가는 지점이 있다. `Projectile.DamageDealt`는 누구나 구독할 수 있는
    // public static event이고(#169 보상 효과는 지금도 확장 중인 축), 구독자가 같은 콜스택에서 다른
    // 타워의 발사를 유발할 수 있다. 상태가 static이면 그 중첩 호출이 진행 중인 순회의 hitSet을
    // 비워, 이미 때린 적을 다시 고르거나 두 적 사이를 왕복하게 된다 — 에러 없이 데미지 분포만 틀어져
    // 재현이 어렵다. 소유자(타워)마다 인스턴스를 두면 그 간섭이 구조적으로 불가능해진다.
    public sealed class ChainResolver
    {
        // 중복 타격 방지. 순회 전체에 걸쳐 살아 있고 그 사이에 외부 호출이 있으므로 공유하면 안 된다.
        readonly HashSet<IDamageable> hitSet = new HashSet<IDamageable>();

        // 대상 수집 버퍼. 채우고 읽는 사이에 외부 호출이 없어 재진입에 노출되지 않지만,
        // 같은 소유자 상태로 묶어 두는 편이 수명이 명확하다.
        // NonAlloc은 버퍼 크기를 넘는 결과를 조용히 버린다. 체인 반경은 좁아 32면 충분하다(WL-025).
        readonly Collider[] hitBuffer = new Collider[32];

        // 통지 대기열. 순회 중에는 담기만 하고 종료 후 한 번에 발행한다(아래 Resolve 주석 참조).
        readonly List<IDamageable> victims = new List<IDamageable>();

        /// 최초 대상부터 최대 maxTargets명까지 튕기며 데미지를 적용한다.
        /// outPath(null 허용)에는 실제로 피해를 입은 대상의 좌표가 타격 순서대로 담긴다.
        public void Resolve(
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
            victims.Clear();
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
                hitSet.Add(current);
                victims.Add(current);   // 통지는 순회 종료 후

                if (hop + 1 >= limit) break;

                IDamageable next = FindNearestUnhit(pos, chainRadius, enemyMask, source.Faction);
                if (next == null) break;

                current = next;
                dmg *= damageFalloff;   // 튕길 때마다 ×falloff 누적
            }

            // 통지는 순회가 끝난 뒤 발행한다. 구독자가 무엇을 할지 알 수 없으므로(체인을 하나 더
            // 유발할 수도 있다), hitSet을 읽는 구간이 끝난 뒤로 미뤄 순회 결과가 오염되지 않게 한다.
            //
            // 인덱스 순회 + 사후 Clear인 이유: foreach면 재진입이 목록을 수정할 때
            // InvalidOperationException이 난다. Count를 매 반복 다시 읽으므로 목록이 늘어도 안전하다.
            //
            // 순회 중 남는 외부 호출은 TakeDamage 하나다(OnHpChanged 발행 + 사망 시 Die).
            // 그 경로의 현재 구독자는 HP 바뿐이라 발사를 유발하지 않는다.
            for (int i = 0; i < victims.Count; i++)
            {
                Projectile.RaiseDamageDealt(source, victims[i]);
            }

            victims.Clear();
        }

        // center 반경 내, 아직 안 맞은 가장 가까운 적
        IDamageable FindNearestUnhit(Vector3 center, float radius, LayerMask mask, Faction sourceFaction)
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
