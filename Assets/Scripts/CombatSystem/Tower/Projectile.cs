using System;
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
        public float StunDuration;        // >0이면 명중 대상에 스턴(초) 부여(#164 소다타워). 타워가 발사 시 세팅

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

    // 투사체 비행 방식
    //  Homing   : 매 프레임 살아있는 대상을 추적하는 유도탄 (반드시 명중). arcHeight>0이면 곡사(포물선) 비주얼도 적용.
    //  Ballistic: 발사 순간 대상의 월드 위치를 착탄점으로 고정하고 그 지점까지 비행.
    //             대상이 도중에 죽거나 움직여도 고정된 착탄점에 그대로 명중(박격포 등).
    public enum FlightMode { Homing, Ballistic }

    public class Projectile : MonoBehaviour
    {
        // 투사체 데미지가 실제로 들어간 직후 통지(공격자, 피격자) — #169 버프 특수효과(BurnBuff 등)가
        // 구독한다. 순수 추가 훅으로 기존 공격 로직은 무수정. static이므로 구독자가 해제를 책임진다.
        public static event Action<IAttacker, IDamageable> DamageDealt;

        [SerializeField] Vector3 rotationOffset;

        [Header("Flight")]
        [SerializeField] FlightMode flightMode = FlightMode.Homing;
        // Ballistic 전용: 착탄점까지의 포물선 정점 높이(월드 단위). 0이면 직선.
        [SerializeField] float arcHeight = 0f;

        IDamageable target;
        float damage;
        float speed;
        IAttacker source;
        ProjectileImpact impact;

        // Ballistic 상태: 발사 순간 스냅샷한 착탄점과 시작점, 진행 거리
        Vector3 startPos;
        Vector3 landingPos;
        float totalDistance;   // 발사 시 시작점→대상까지 거리(호밍 아크 진행도 계산에도 재사용)
        float traveled;

        // Homing 상태: 아크를 얹기 전의 "평면" 추적 위치. transform.position = homingPos + 아크 높이.
        Vector3 homingPos;

        // 체인 중복 타격 방지용 (한 프레임에 하나의 투사체만 명중 처리되므로 static 재사용 OK)
        static readonly HashSet<IDamageable> chainHitSet = new HashSet<IDamageable>();

        public void Init(IDamageable target, float damage, float speed, IAttacker source, ProjectileImpact impact)
        {
            this.target = target;
            this.damage = damage;
            this.speed = speed;
            this.source = source;
            this.impact = impact;

            if (flightMode == FlightMode.Ballistic)
            {
                // 발사 순간의 대상 위치를 착탄점으로 고정 (이후 대상 이동/사망과 무관)
                startPos = transform.position;
                landingPos = target.HitPosition.position;
                totalDistance = Vector3.Distance(startPos, landingPos);
            }
            else // Homing
            {
                // 평면 추적 시작점 + 초기 거리(아크 진행도 t 계산 기준) 스냅샷.
                homingPos = transform.position;
                var at = target.HitPosition;
                Vector3 tp = at.transform.position;
                totalDistance = Vector3.Distance(homingPos, tp);
            }
        }

        void Update()
        {
            if (flightMode == FlightMode.Ballistic)
                UpdateBallistic();
            else
                UpdateHoming();
        }

        // 살아있는 대상을 매 프레임 추적하는 유도탄. arcHeight>0이면 평면 추적 위에 포물선 높이를 얹어 곡사로 보이게 한다.
        void UpdateHoming()
        {
            var targetTransform = target.HitPosition;
            if (targetTransform == null || target.IsDead)
            {
                Destroy(gameObject);   // 대상이 도중에 사라지면 소멸
                return;
            }

            Vector3 targetPos = targetTransform.position;
            Vector3 prevPos = transform.position;

            // 평면(비아크) 추적 위치를 대상으로 이동. 아크는 이 위에 시각적 높이만 더한다.
            homingPos = Vector3.MoveTowards(homingPos, targetPos, speed * Time.deltaTime);

            // 진행도 t: 시작 시 0, 대상에 근접할수록 1(초기 거리 기준). 대상이 멀어지면 0으로 clamp.
            float remaining = Vector3.Distance(homingPos, targetPos);
            float t = totalDistance > 0.0001f ? Mathf.Clamp01(1f - remaining / totalDistance) : 1f;
            float arcY = arcHeight * 4f * t * (1f - t);   // 양 끝 0, t=0.5에서 정점(arcHeight)인 포물선

            transform.position = homingPos + Vector3.up * arcY;

            // 실제 이동 방향(아크 포함)을 향하도록 회전 → 곡사 시 상승/하강에 맞춰 기수가 기운다.
            Vector3 moveDir = transform.position - prevPos;
            if (moveDir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(moveDir.normalized) * Quaternion.Euler(rotationOffset);

            if (remaining < 0.1f)
            {
                OnHit(targetPos);
                Destroy(gameObject);
            }
        }

        // 고정된 착탄점까지 (선택적 포물선으로) 비행. 대상 생사와 무관.
        void UpdateBallistic()
        {
            Vector3 prevPos = transform.position;

            traveled += speed * Time.deltaTime;
            float t = totalDistance > 0.0001f ? Mathf.Clamp01(traveled / totalDistance) : 1f;

            Vector3 pos = Vector3.Lerp(startPos, landingPos, t);
            pos.y += arcHeight * 4f * t * (1f - t);   // t=0.5에서 정점(arcHeight), 양 끝 0인 포물선
            transform.position = pos;

            Vector3 dir = pos - prevPos;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(rotationOffset);

            if (t >= 1f)
            {
                OnHit(landingPos);
                Destroy(gameObject);
            }
        }

        void OnHit(Vector3 impactPos)
        {
            switch (impact.Kind)
            {
                case ImpactKind.Area:
                    ApplyArea(impactPos);   // 위치 기반: 대상 생사와 무관
                    break;
                case ImpactKind.Chain:
                    // 체인은 살아있는 최초 대상이 필요 (Ballistic에서 대상이 죽었으면 명중 없음)
                    if (target != null && !target.IsDead) ApplyChain();
                    break;
                default:
                    if (target != null && !target.IsDead)
                    {
                        target.TakeDamage(new DamageInfo(damage, source));
                        DamageDealt?.Invoke(source, target);
                        if (impact.StunDuration > 0f) ApplyStun(target);
                    }
                    break;
            }
        }

        // 명중 시 스턴 부여(#164). CC 인프라 공유: 배율 0을 넘기면 StatusEffectHandler가 속도 축이 아니라
        // 스턴 축(IMovementAgent.AddStun)으로 보낸다 — 속도 축은 하한 클램프 때문에 배율 0으로도 멈추지 않는다.
        // 공유 effectId라 중첩 없이 갱신만 된다.
        static readonly int StunEffectId = "onhit.stun".GetHashCode();
        void ApplyStun(IDamageable enemy)
        {
            if (impact.StunDuration <= 0f) return;
            if (enemy is not Component c) return;

            var handler = c.GetComponent<StatusEffectHandler>();
            if (handler == null) handler = c.gameObject.AddComponent<StatusEffectHandler>();
            handler.ApplySlow(StunEffectId, 0f, impact.StunDuration);
        }

        // 명중 지점 반경 내 모든 적에게 동일 데미지
        void ApplyArea(Vector3 impactPos)
        {
            var hits = Physics.OverlapSphere(impactPos, impact.SplashRadius, impact.EnemyMask);
            foreach (var h in hits)
            {
                var d = h.GetComponentInParent<IDamageable>();
                if (d != null && d.Faction != source.Faction && !d.IsDead)
                {
                    d.TakeDamage(new DamageInfo(damage, source));
                    DamageDealt?.Invoke(source, d);
                }
            }
        }

        // 대상 → 인근 적으로 튕기며 홉마다 데미지 *= falloff
        void ApplyChain()
        {
            chainHitSet.Clear();

            float dmg = damage;
            target.TakeDamage(new DamageInfo(dmg, source));   // 최초 대상: 풀 데미지
            DamageDealt?.Invoke(source, target);
            chainHitSet.Add(target);

            Vector3 from = target.HitPosition.transform.position;

            for (int i = 1; i < impact.MaxChainTargets; i++)
            {
                var next = FindNearestUnhit(from, impact.ChainRadius);
                if (next == null) break;

                dmg *= impact.ChainDamageFalloff;             // 튕길 때마다 ×0.8 누적
                next.TakeDamage(new DamageInfo(dmg, source));
                DamageDealt?.Invoke(source, next);
                chainHitSet.Add(next);
                from = next.HitPosition.transform.position;
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
                float sqr = (d.HitPosition.position - center).sqrMagnitude;   
                if (sqr < closestSqr) { closestSqr = sqr; closest = d; }
            }
            return closest;
        }
    }
}