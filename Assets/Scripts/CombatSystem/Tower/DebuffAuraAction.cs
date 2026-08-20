using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 반경 안의 적에게 효과를 지속 부여하는 오라. 상태의 소유는 대상의 StatusEffectHandler다 —
    // 대상이 반경을 벗어나 갱신이 끊겨도 남은 Duration만큼 효과가 흐르다 만료된다.
    //
    // 적이 계속 움직이므로 주기적 재스캔이 필수다(버프 오라와 달리 이벤트로 접을 수 없다).
    //
    // **거는 효과는 공격 액션과 같은 `TowerAsset.Effects`를 쓴다**(#274 Phase 4). "맞으면 화상"과
    // "장판에 화상"은 거는 방식만 다르고 효과 자체는 같기 때문이다 — 덕분에 화상 장판 타워가
    // 새 코드 없이 만들어진다. 예전에는 `DebuffAuraFields`에 DoT·감속이 수기 필드로 따로 박혀 있었다.
    [Serializable]
    public sealed class DebuffAuraAction : TowerAction
    {
        [NonSerialized] TowerAsset.DebuffAuraFields aura;
        [NonSerialized] List<HitEffect> effects;
        [NonSerialized] LayerMask targetLayerMask;
        [NonSerialized] float tickTimer;
        [NonSerialized] Collider[] hitBuffer;

        public override TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        // 오라 반경은 **자기 축**(TowerStat.AuraRadius)을 쓴다. 예전에는 공격 사거리 축을 공유해
        // "사거리 개념을 둘로 두지 않는다"를 만족했는데, 버프 타일이 증가량을 칸 단위 절댓값으로
        // 약속하는 순간 그 전제가 깨졌다 — 오라 기본 반경(1.6~2.7칸)이 공격 사거리(3칸)보다 작아
        // 같은 "+3칸"이 오라에서는 지름 3배가 됐다(장판이 상시 보이니 그대로 드러난다).
        //
        // 축만 갈렸고 단일 출처는 유지된다 — 판정(ApplyDebuff)·선택 원·장판(AuraZoneVisual)이
        // 전부 이 프로퍼티 하나를 본다.
        public float Radius =>
            aura == null ? 0f : Owner.Stats.Evaluate(TowerStat.AuraRadius, aura.Radius);

        // 선택 사거리 원은 오라 반경을 그린다 — 배치 프리뷰 원(TowerPlacer가 PreviewRadius로 그림)과
        // 같은 값이라 "놓을 때 보이던 원이 선택하면 사라진다"는 비일관이 생기지 않는다.
        public override float DisplayRange => Radius;

        // 재스캔 주기. 0 이하 폭주 방지 하한.
        //
        // 이 값은 원장을 거치지 않는다(공속 modifier에 반응하지 않음). 재스캔을 빠르게 해도 DoT는 이미
        // 대상이 소유하고 있어 갱신만 더 자주 될 뿐 피해가 늘지 않기 때문이다 — 독 타워에서 "공속"의
        // 의미를 갖는 축은 효과 쪽 TickInterval이고, 그건 HitEffect가 원장을 거쳐 합성한다.
        float Interval => Mathf.Max(aura != null ? aura.Interval : 0f, 0.05f);

        protected override void OnInitialize(TowerAsset asset)
        {
            aura = asset.DebuffAura;
            effects = asset.Effects;
            targetLayerMask = Owner.EnemyLayerMask;
            hitBuffer ??= new Collider[32];

            tickTimer = 0f;   // 밤 진입 직후 첫 Tick에서 즉시 1회 적용
        }

        public override void Dispose()
        {
            // 부여한 효과를 회수하지 않는다 — 대상이 Duration을 스스로 소진하는 설계이므로,
            // 타워가 철거되면 남은 시간만큼 효과가 흐르다 만료되는 게 의도된 거동이다.
            tickTimer = 0f;
        }

        public override void Tick(float deltaTime)
        {
            if (aura == null || effects == null || effects.Count == 0) return;

            tickTimer -= deltaTime;
            if (tickTimer > 0f) return;

            tickTimer = Interval;
            ApplyDebuff();
        }

        void ApplyDebuff()
        {
            int count = Physics.OverlapSphereNonAlloc(
                Origin.position, Radius, hitBuffer, targetLayerMask);
            if (count == 0) return;

            // 소스 키는 HitEffect.SourceKey — 투사체 경로(Projectile.ApplyEffects)와 **같은 함수**라,
            // "맞아서 걸린 화상"과 "장판에서 걸린 화상"이 같은 슬롯을 쓴다.
            int baseId = Owner.GetInstanceID();
            TowerStats stats = Owner.Stats;

            for (int i = 0; i < count; i++)
            {
                IDamageable damageable = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (damageable == null || damageable.IsDead) continue;
                if (damageable.Faction == Owner.Faction) continue;   // 디버프는 적군만

                for (int e = 0; e < effects.Count; e++)
                {
                    HitEffect effect = effects[e];
                    if (effect == null) continue;

                    // 합성 계승(#274 Phase 5) — 투사체 경로(Projectile.ApplyEffects)와 같은 규칙이다.
                    if (!Owner.IsEffectActive(effect.Kind)) continue;

                    effect.Apply(damageable, Owner, stats, HitEffect.SourceKey(baseId, effect.Kind));
                }
            }
        }

        // 정보 패널: 반경 + 효과 요약. 효과 수치는 **실효값**(원장 합성 후)이라 표기와 실제가 일치한다.
        public override string DescribeStats()
            => aura == null
                ? null
                : TowerStatsFormatter.Join(
                    TowerStatsFormatter.BuildRangeLine(Radius),
                    AttackAction.DescribeEffects(effects, Owner));

#if UNITY_EDITOR
        public override void DrawGizmos()
        {
            if (aura == null) return;
            UnityEditor.Handles.color = new Color(0.6f, 0.2f, 0.85f);
            UnityEditor.Handles.DrawWireDisc(Origin.position, Vector3.up, Radius);
        }
#endif
    }
}
