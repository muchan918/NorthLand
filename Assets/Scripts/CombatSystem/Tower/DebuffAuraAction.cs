using System;
using UnityEngine;

namespace NorthLand.Combat
{
    // 반경 안의 적에게 DoT·슬로우를 지속 부여하는 오라. 상태의 소유는 대상의 StatusEffectHandler다 —
    // 대상이 반경을 벗어나 갱신이 끊겨도 남은 Duration만큼 효과가 흐르다 만료된다.
    //
    // 적이 계속 움직이므로 주기적 재스캔이 필수다(버프 오라와 달리 이벤트로 접을 수 없다).
    [Serializable]
    public sealed class DebuffAuraAction : TowerAction
    {
        [NonSerialized] TowerAsset.DebuffAuraFields aura;
        [NonSerialized] LayerMask targetLayerMask;
        [NonSerialized] float tickTimer;
        [NonSerialized] Collider[] hitBuffer;

        public override TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        // 오라 반경은 공격 사거리와 같은 원장 축을 쓴다 — "사거리"라는 개념을 둘로 두지 않기 위함.
        // 기본값만 다르다(SO의 DebuffAura.Radius). 타일 버프의 사거리 증가가 오라에도 그대로 반영된다.
        public float Radius =>
            aura == null ? 0f : Owner.Stats.Evaluate(TowerStat.AttackRange, aura.Radius);

        // 선택 사거리 원은 오라 반경을 그린다 — 배치 프리뷰 원(TowerPlacer가 PreviewRadius로 그림)과
        // 같은 값이라 "놓을 때 보이던 원이 선택하면 사라진다"는 비일관이 생기지 않는다.
        public override float DisplayRange => Radius;

        // 0 이하 폭주 방지 하한.
        //
        // 이 값은 원장을 거치지 않는다(공속 modifier에 반응하지 않음). 재스캔 주기를 빠르게 해도
        // DoT는 이미 대상이 소유하고 있어 갱신만 더 자주 될 뿐 피해가 늘지 않기 때문이다 —
        // 독 타워에서 "공속"의 의미를 갖는 축은 DotTickInterval(아래)이다.
        float Interval => Mathf.Max(aura != null ? aura.Interval : 0f, 0.05f);

        // ── 원장이 합성한 실효 DoT 수치 ────────────────────────────────────
        // 예전에는 SO 값을 그대로 대상에 넘겨, 타일 버프의 공격력·공속이 오라에 전혀 먹히지 않았다.
        // 사거리(Radius)만 원장을 거쳐서 "사거리 타일은 먹히는데 공격력 타일은 무반응"이라는
        // 예측 불가능한 비대칭이 있었다. 세 축을 모두 원장에 연결해 그 구멍을 닫는다.
        bool HasDot => aura?.Damage != null && aura.Damage.HasDamage;

        float DotDamage =>
            HasDot ? Owner.Stats.Evaluate(TowerStat.AttackDamage, aura.Damage.DamageAmount) : 0f;

        // 공속이 오를수록 틱 간격이 짧아진다(공격 타워의 AttackInterval과 같은 규칙).
        float DotTickInterval =>
            HasDot
                ? aura.Damage.TickInterval / Mathf.Max(Owner.Stats.Evaluate(TowerStat.AttackSpeed, 1f), 0.01f)
                : 0f;

        protected override void OnInitialize(TowerAsset asset)
        {
            aura = asset.DebuffAura;
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
            if (aura == null) return;

            tickTimer -= deltaTime;
            if (tickTimer > 0f) return;

            tickTimer = Interval;
            ApplyDebuff();
        }

        void ApplyDebuff()
        {
            // 실효값을 루프 밖에서 한 번 계산한다(대상 수와 무관하게 같은 값).
            bool hasDot = HasDot;
            float dotDamage = DotDamage;
            float dotTickInterval = DotTickInterval;

            // 슬로우 강도는 원장을 거치지 않는다 — TowerStat에 "CC 강도" 축이 없고, 감속을
            // 공격력/공속 축에 억지로 매핑하면 의미가 어긋난다. 필요해지면 축 신설이 선행 과제다(WL-127).
            float slowMultiplier = AuraModifiers.ComputeSlowMultiplier(aura.Modifiers);
            bool hasSlow = slowMultiplier < 1f;

            if (!hasDot && !hasSlow) return;   // 적용할 효과 없음

            int effectId = SourceId;
            int count = Physics.OverlapSphereNonAlloc(
                Origin.position, Radius, hitBuffer, targetLayerMask);

            for (int i = 0; i < count; i++)
            {
                IDamageable damageable = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (damageable == null || damageable.IsDead) continue;
                if (damageable.Faction == Owner.Faction) continue;   // 디버프는 적군만

                if (damageable is not Component target) continue;

                if (!target.TryGetComponent(out StatusEffectHandler handler))
                {
                    handler = target.gameObject.AddComponent<StatusEffectHandler>();
                }

                if (hasDot) handler.ApplyOrRefresh(effectId, dotDamage, dotTickInterval, aura.Duration, Owner);
                if (hasSlow) handler.ApplySlow(effectId, slowMultiplier, aura.Duration);
            }
        }

        // 정보 패널에 이 오라가 기여할 줄. 반경 + 효과 요약(DoT / 슬로우).
        // 세 값 모두 **실효값**(원장 합성 후)이라 패널 표기와 실제 효과가 일치한다.
        public override string DescribeStats()
            => aura == null
                ? null
                : TowerStatsFormatter.Join(
                    TowerStatsFormatter.BuildRangeLine(Radius),
                    TowerStatsFormatter.BuildDotLine(DotDamage, DotTickInterval),
                    TowerStatsFormatter.BuildSlowLine(AuraModifiers.ComputeSlowMultiplier(aura.Modifiers)));

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
