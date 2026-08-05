using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 투사체 명중 시 동작 종류.
    // 체인은 여기 없다 — 히트스캔으로 전달되므로 투사체가 배달하지 않는다(#252, HitscanAttackAction).
    public enum ImpactKind { Single, Area }

    // 타워가 발사 시 넘기는 "명중하면 어떻게 터질지" 기술자
    public struct ProjectileImpact
    {
        public ImpactKind Kind;
        public LayerMask EnemyMask;
        public float SplashRadius;        // Area

        public static ProjectileImpact MakeSingle()
            => new ProjectileImpact { Kind = ImpactKind.Single };

        public static ProjectileImpact MakeArea(float splashRadius, LayerMask mask)
            => new ProjectileImpact { Kind = ImpactKind.Area, SplashRadius = splashRadius, EnemyMask = mask };
    }

    // 비행 축은 부품으로 분리돼 있다 → ProjectileFlight.cs
    //  구 `FlightMode` enum + Update의 분기는 #274 Phase 4.5에서 사라졌다. 새 비행 방식은
    //  `ProjectileFlight` 파생 클래스 1개이며 이 파일은 무수정이다.

    public class Projectile : MonoBehaviour
    {
        // 타워의 **직접 타격** 데미지가 실제로 들어간 직후 통지(공격자, 피격자) — #169 버프 특수효과
        // (BurnBuff 등)가 구독한다. 순수 추가 훅으로 기존 공격 로직은 무수정.
        // static이므로 구독자가 해제를 책임진다.
        public static event Action<IAttacker, IDamageable> DamageDealt;

        // 투사체 밖의 명중 해결기(ChainResolver 등)가 위 이벤트를 발행하는 창구.
        // C# 이벤트는 선언한 클래스 밖에서 Invoke할 수 없어 이 우회가 필요하다.
        //
        // ⚠ 직접 타격만 통지한다 — 지속 효과(오라 DoT)의 틱에서는 부르면 안 된다.
        //   구독자가 명중마다 화상 지속시간을 갱신하므로(BurnBuff.HandleTowerDamage), 틱마다 통지하면
        //   갱신이 끊기지 않아 사실상 영구 화상이 된다. 이 경계를 지키는 건 호출부 책임이다.
        //
        // TODO(#252): DamageDealt는 "전투 데미지 통지"라 본래 투사체 소유가 아니다 —
        //             중립 위치로 옮기는 것이 정공법이고, 이 창구는 그때까지의 임시 우회다.
        internal static void RaiseDamageDealt(IAttacker source, IDamageable target)
            => DamageDealt?.Invoke(source, target);

        // 프리팹에 남는 **유일한** 설정 — 모델 메시의 기수가 어느 축을 보는지 보정한다(화살 −90, 공 0).
        // 타워가 알 이유가 없는 값이라 여기 남는다. 비행·명중은 전부 타워 SO가 정한다(#274).
        [SerializeField] Vector3 rotationOffset;

        IDamageable target;
        float damage;
        IAttacker source;
        ProjectileFlight flight;
        ProjectileImpact impact;

        // 명중 시 걸 효과(화상·독·감속·스턴). SO의 TowerAsset.Effects를 발사 시 그대로 넘겨받는다.
        IReadOnlyList<HitEffect> effects;

        // 비행 진행 상태. **부품이 아니라 이쪽이 소유한다** — 부품은 SO에 살아 여러 투사체가
        // 공유하므로 진행값을 두면 서로 덮어쓴다(FlightState 주석 참조).
        FlightState flightState;

        public void Init(IDamageable target, float damage, IAttacker source,
                         ProjectileFlight flight, ProjectileImpact impact,
                         IReadOnlyList<HitEffect> effects = null)
        {
            this.target = target;
            this.damage = damage;
            this.source = source;
            this.flight = flight;
            this.impact = impact;
            this.effects = effects;

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
                default:
                    if (target != null && !target.IsDead) Hit(target, damage);
                    break;
            }
        }

        /// 한 대상에 대한 명중 처리 전부 — 데미지 + 통지 + 효과.
        ///
        /// **투사체의 두 명중 경로(Single/Area)가 전부 여기를 지난다.** 예전에는 스턴이 Single 경로에서만
        /// 적용돼, Area 타워 SO에 `OnHitStunDuration`을 저작하면 예외 없이 조용히 무시됐다
        /// (#274 Phase 4에서 구조적으로 해소).
        ///
        /// ⚠ **체인은 이 경로를 지나지 않는다** — 히트스캔으로 전달되므로 `ChainResolver`가 데미지·통지를
        /// 직접 처리하고 **명중 효과는 걸지 않는다**(#252의 확정 결정, 근거는 그쪽 주석).
        /// 그 어긋남이 조용해지지 않도록 `TowerAsset.OnValidate`가 저장 시점에 경고한다.
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
        void ApplyArea(Vector3 impactPos)
        {
            var hits = Physics.OverlapSphere(impactPos, impact.SplashRadius, impact.EnemyMask);
            foreach (var h in hits)
            {
                var d = h.GetComponentInParent<IDamageable>();
                if (d != null && d.Faction != source.Faction && !d.IsDead) Hit(d, damage);
            }
        }
    }
}