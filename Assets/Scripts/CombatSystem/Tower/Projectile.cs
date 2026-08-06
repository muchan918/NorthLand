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

    // 비행 축은 부품으로 분리돼 있다 → ProjectileFlight.cs
    //  구 `FlightMode` enum + Update의 분기는 #274 Phase 4.5에서 사라졌다. 새 비행 방식은
    //  `ProjectileFlight` 파생 클래스 1개이며 이 파일은 무수정이다.

    public class Projectile : MonoBehaviour
    {
        // 투사체 데미지가 실제로 들어간 직후 통지(공격자, 피격자) — #169 버프 특수효과(BurnBuff 등)가
        // 구독한다. 순수 추가 훅으로 기존 공격 로직은 무수정. static이므로 구독자가 해제를 책임진다.
        public static event Action<IAttacker, IDamageable> DamageDealt;

        // 프리팹에 남는 **유일한** 설정 — 모델 메시의 기수가 어느 축을 보는지 보정한다(화살 −90, 공 0).
        // 타워가 알 이유가 없는 값이라 여기 남는다. 비행·명중은 전부 타워 SO가 정한다(#274).
        [SerializeField] Vector3 rotationOffset;

        IDamageable target;
        float damage;
        IAttacker source;
        ProjectileFlight flight;
        ProjectileImpact impact;

        // 비행 부품(StraightFlight 등)이 자체 접촉 판정에 쓰는 읽기 창구. Tower의 enemyLayerMask →
        // AttackAction.BuildImpact → impact.EnemyMask로 이미 흘러온 값을 그대로 노출한다 — 비행 부품에
        // 마스크를 따로 저작시키면 같은 값이 SO 두 곳(Tower와 Flight)에 살아 어긋날 수 있다(#298).
        public LayerMask EnemyMask => impact.EnemyMask;

        // 비행 부품이 "얼마나 멀리 갈 수 있는가"의 기준으로 쓰는 읽기 창구. AttackAction.Range는
        // 원장 합성값(사거리 버프 반영)인데, 이걸 비행 부품에 고정 수치로 따로 저작시키면 사거리 버프를
        // 받아도 실제 비행 거리는 그대로인 어긋남이 생긴다(#298) — EnemyMask와 같은 이유·같은 해법.
        public float Range { get; private set; } = float.MaxValue;

        // 명중 시 걸 효과(화상·독·감속·스턴). SO의 TowerAsset.Effects를 발사 시 그대로 넘겨받는다.
        IReadOnlyList<HitEffect> effects;

        // 비행 진행 상태. **부품이 아니라 이쪽이 소유한다** — 부품은 SO에 살아 여러 투사체가
        // 공유하므로 진행값을 두면 서로 덮어쓴다(FlightState 주석 참조).
        FlightState flightState;

        // 체인 중복 타격 방지용 (한 프레임에 하나의 투사체만 명중 처리되므로 static 재사용 OK)
        static readonly HashSet<IDamageable> chainHitSet = new HashSet<IDamageable>();

        public void Init(IDamageable target, float damage, IAttacker source,
                         ProjectileFlight flight, ProjectileImpact impact,
                         IReadOnlyList<HitEffect> effects = null, float range = float.MaxValue)
        {
            this.target = target;
            this.damage = damage;
            this.source = source;
            this.flight = flight;
            this.impact = impact;
            this.effects = effects;
            this.Range = range;

            if (flight == null)
            {
                Debug.LogError("[Projectile] ProjectileFlight 없이 발사됐습니다 — 타워 SO의 Attack.Flight를 확인하세요.", this);
                Destroy(gameObject);
                return;
            }

            flight.Begin(this, target, ref flightState);
        }

        // **분기가 없다.** 어떻게 나는지는 부품이 정하고, 호스트는 그 결과(명중/수명)만 해석한다.
        // 새 비행 방식이 추가돼도 이 메서드는 안 바뀐다(#274 Phase 4.5).
        void Update()
        {
            if (flight == null) return;

            Vector3 prevPos = transform.position;

            FlightStep step = flight.Step(this, target, ref flightState, Time.deltaTime);

            // 기수 회전은 호스트가 한 곳에서 한다 — 부품은 위치만 정한다.
            // 실제 이동 방향(아크 포함)을 향하므로 곡사에서 상승/하강에 맞춰 기울어진다.
            Vector3 moveDir = transform.position - prevPos;
            if (moveDir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(moveDir.normalized) * Quaternion.Euler(rotationOffset);

            // ★ Impact와 Finished는 독립이다 — 때리고도 계속 나는 탄(관통·부메랑)이 이 분리로 성립한다.
            if (step.Impact) OnHit(step.ImpactPos);
            if (step.Finished) Destroy(gameObject);
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
                    if (target != null && !target.IsDead) Hit(target, damage);
                    break;
            }
        }

        /// 한 대상에 대한 명중 처리 전부 — 데미지 + 통지 + 효과.
        ///
        /// **세 명중 경로(Single/Area/Chain)가 전부 여기를 지난다.** 예전에는 스턴이 Single 경로에서만
        /// 적용돼, Area/Chain 타워 SO에 `OnHitStunDuration`을 저작하면 예외 없이 조용히 무시됐다
        /// (#274 Phase 4에서 구조적으로 해소).
        void Hit(IDamageable victim, float amount)
        {
            victim.TakeDamage(new DamageInfo(amount, source));
            DamageDealt?.Invoke(source, victim);
            ApplyEffects(victim);
        }

        void ApplyEffects(IDamageable victim)
        {
            if (effects == null || effects.Count == 0) return;

            // 소스 키 채번은 HitEffect.SourceKey 하나만 쓴다 — 오라 경로와 규칙이 갈라지면
            // 같은 효과가 두 슬롯을 잡거나 서로 덮어쓴다(그 자리의 실패 이력은 그 함수 주석 참조).
            int baseId = source is Component c ? c.GetInstanceID() : 0;

            // 쏜 쪽이 타워면 원장(DoT 수치가 타일 버프·오라 버프를 타도록)과 계승 필터를 함께 본다.
            // 적이 쏜 경우엔 둘 다 없다(스코프 밖에서 쓰므로 삼항 패턴 변수가 아니라 지역 변수로 둔다).
            Tower tower = source as Tower;
            TowerStats stats = tower != null ? tower.Stats : null;

            for (int i = 0; i < effects.Count; i++)
            {
                HitEffect effect = effects[i];
                if (effect == null) continue;   // [SerializeReference] rename 시 null 항목이 생긴다

                // 합성 계승(#274 Phase 5): 결과 타워는 SO에 정의된 효과 중 **재료가 물려준 종류만** 낸다.
                // 평범하게 배치된 타워는 필터가 없어 전부 통과한다. 목록을 미리 거르지 않고 여기서 묻는
                // 이유는 Build가 계승 부여보다 먼저 돌기 때문이다(Tower.IsEffectActive 주석 참조).
                if (tower != null && !tower.IsEffectActive(effect.Kind)) continue;

                effect.Apply(victim, source, stats, HitEffect.SourceKey(baseId, effect.Kind));
            }
        }

        // 명중 지점 반경 내 모든 적에게 동일 데미지
        //
        // **"이번 구간에 이미 맞았는가"의 판정도 여기서 한다**(#298). 여러 번 때리며 나는 탄(부메랑)의
        // 중복 방지를 비행 부품 쪽에 두면 그쪽 기준(`HitRadius`)과 이쪽 기준(`SplashRadius`)이 갈라져,
        // 두 반경의 차이만큼 중복이 샌다 — 밀집 대형에서 A가 걸려 A·B가 맞고 곧이어 B가 걸려 A가 또
        // 맞았다. 원장은 비행 상태(구간 개념이 거기 있다)에 두되 **채우는 것은 실제로 때리는 이쪽**이며,
        // 이것이 `FlightStep.Impact`가 명문화한 "무엇을 때릴지는 명중 축이 정한다" 규약이다.
        void ApplyArea(Vector3 impactPos)
        {
            var hits = Physics.OverlapSphere(impactPos, impact.SplashRadius, impact.EnemyMask);
            foreach (var h in hits)
            {
                var d = h.GetComponentInParent<IDamageable>();
                if (d == null || d.Faction == source.Faction || d.IsDead) continue;

                // 왕복 비행만 이 원장을 든다 — 나머지는 null이라 무조건 통과한다(첫 명중에 소멸하므로
                // 중복 자체가 없다). Add()가 false = 이번 구간에 이미 피해를 입힌 대상.
                if (flightState.LegHitSet != null && !flightState.LegHitSet.Add(d)) continue;

                Hit(d, damage);
            }
        }

        // 대상 → 인근 적으로 튕기며 홉마다 데미지 *= falloff
        void ApplyChain()
        {
            chainHitSet.Clear();

            float dmg = damage;
            Hit(target, dmg);                                 // 최초 대상: 풀 데미지
            chainHitSet.Add(target);

            Vector3 from = target.HitPosition.transform.position;

            for (int i = 1; i < impact.MaxChainTargets; i++)
            {
                var next = FindNearestUnhit(from, impact.ChainRadius);
                if (next == null) break;

                dmg *= impact.ChainDamageFalloff;             // 튕길 때마다 ×0.8 누적
                Hit(next, dmg);
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