using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 다수 타겟 동시 잠금 + 균일 지속딜(#298). 투사체가 없는 새 행동 축이라 액션 파생이 맞다.
    //
    // ⚠ 잠금은 **지속형(sticky)**이다 — 한 번 잠근 대상은 죽거나 사거리를 벗어나기 전까지 계속
    // 유지되고, 빈 슬롯만 새 대상으로 채운다(`MaintainLocks`). 매 틱 전체를 다시 뽑는 방식이 아니다 —
    // 그러면 사거리 안 대상 구성이 그대로여도 물리 엔진 내부 순서에 따라 잠금이 흔들린다.
    //
    // ⚠ 범위 조정: 대상별 유지 시간에 따라 피해가 늘어나는 램프업은 이 액션의 범위 밖이다 — 별도
    // "램프업 타워" 이슈에서 공용 부품(가칭 램프 계수 제공자)으로 구현해 이 액션이 선택적으로 참조하는
    // 형태로 확장할 예정이다. 지금은 매 틱 균일 계수로만 피해를 준다(대상마다 가중치 없음).
    [Serializable]
    public sealed class BeamAction : TowerAction
    {
        // ── 런타임 상태 (직렬화 금지 — TowerAction 규칙 ③) ──────────────────
        [NonSerialized] TowerAsset.BeamFields fields;
        [NonSerialized] List<HitEffect> effects;
        [NonSerialized] LayerMask enemyLayerMask;
        [NonSerialized] float tickTimer;
        [NonSerialized] Collider[] hitBuffer;
        [NonSerialized] List<IDamageable> lockedTargets;

        // 빔 비주얼 — 최소 구현(임시 머티리얼, LineRenderer). 연출 폴리싱은 별도 이슈.
        [NonSerialized] List<LineRenderer> beams;
        [NonSerialized] Material beamMaterial;

        public override TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        // 최종 스탯 = SO 기본값 + 원장(Owner.Stats) 합성 — AttackAction과 같은 축을 그대로 쓴다.
        public float Range =>
            fields == null ? 0f : Owner.Stats.Evaluate(TowerStat.AttackRange, fields.Range);

        public float DamagePerTick =>
            fields == null ? 0f : Owner.Stats.Evaluate(TowerStat.AttackDamage, fields.DamagePerTick);

        // 틱 자체가 피해이므로 공속이 오를수록 틱 간격이 짧아진다(AttackAction.Interval과 같은 규칙).
        // DebuffAuraAction의 재스캔 주기가 원장을 안 타는 것과 반대인데, 그쪽은 "재적용 스캔"이라
        // 빨라져도 DoT 자체 피해가 안 늘지만 여기서는 틱마다 직접 데미지를 넣기 때문이다.
        public float TickInterval =>
            fields == null
                ? 0f
                : fields.TickInterval / Mathf.Max(Owner.Stats.Evaluate(TowerStat.AttackSpeed, 1f), 0.01f);

        public override float DisplayRange => Range;

        protected override void OnInitialize(TowerAsset asset)
        {
            fields = asset.Beam;
            effects = asset.Effects;
            enemyLayerMask = Owner.EnemyLayerMask;
            tickTimer = 0f;   // 밤 진입 직후 첫 Tick에서 즉시 1회 적용

            hitBuffer ??= new Collider[32];
            lockedTargets ??= new List<IDamageable>();

            EnsureBeamPool();
        }

        public override void Dispose()
        {
            // 잠금 목록은 인스턴스 소유라 여기서 비우면 재활성화 시 새로 잠근다 — 걸린 효과는 대상 쪽
            // Duration으로 스스로 소진된다(디버프 오라와 같은 설계). 빔 시각 오브젝트는 재사용하도록
            // 숨기기만 한다.
            tickTimer = 0f;
            lockedTargets?.Clear();
            HideAllBeams();
        }

        public override void Tick(float deltaTime)
        {
            if (fields == null || fields.MaxTargets <= 0 || fields.DamagePerTick <= 0f) return;

            // 잠금 유지·보충은 매 프레임 — "죽거나 사거리를 벗어나야만 교체"를 지키려면 빈 슬롯이
            // 생기자마자(다음 틱까지 기다리지 않고) 채워야 한다. 데미지 적용만 TickInterval을 따른다.
            MaintainLocks();
            FollowLockedTargets();

            tickTimer -= deltaTime;
            if (tickTimer > 0f) return;

            tickTimer = TickInterval;
            if (lockedTargets.Count > 0) ApplyToLocked();
        }

        // 지속 잠금(sticky) — 매 틱 다시 뽑지 않는다. 이미 잠근 대상은 죽거나 사거리를 벗어날 때만
        // 목록에서 빠지고, 빈 슬롯만 새 대상으로 채운다. 매 틱 재계산 방식이면 물리 엔진 내부 순서에
        // 따라 사거리 안 대상 구성이 그대로여도 잠금이 흔들릴 수 있어 이 방식으로 바꿨다(#298).
        void MaintainLocks()
        {
            Vector3 origin = Origin.position;
            float sqrRange = Range * Range;

            for (int i = lockedTargets.Count - 1; i >= 0; i--)
            {
                IDamageable v = lockedTargets[i];
                bool outOfRange = v?.HitPosition == null
                    || (v.HitPosition.position - origin).sqrMagnitude > sqrRange;
                if (v == null || v.IsDead || outOfRange) lockedTargets.RemoveAt(i);
            }

            if (lockedTargets.Count >= fields.MaxTargets) return;   // 빈 슬롯 없으면 재탐색 불필요

            int count = Physics.OverlapSphereNonAlloc(origin, Range, hitBuffer, enemyLayerMask);
            for (int i = 0; i < count && lockedTargets.Count < fields.MaxTargets; i++)
            {
                IDamageable d = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (d == null || d.IsDead || d.Faction == Owner.Faction) continue;
                if (lockedTargets.Contains(d)) continue;   // 이미 잠근 대상 중복 추가 방지
                lockedTargets.Add(d);
            }
        }

        void ApplyToLocked()
        {
            int baseId = Owner.GetInstanceID();
            TowerStats stats = Owner.Stats;
            float damage = DamagePerTick;

            for (int i = 0; i < lockedTargets.Count; i++)
            {
                IDamageable victim = lockedTargets[i];
                victim.TakeDamage(new DamageInfo(damage, Owner));

                if (effects == null) continue;
                for (int e = 0; e < effects.Count; e++)
                {
                    HitEffect effect = effects[e];
                    if (effect == null) continue;
                    if (!Owner.IsEffectActive(effect.Kind)) continue;   // 합성 계승 필터(공격 액션과 동일 규칙)

                    effect.Apply(victim, Owner, stats, HitEffect.SourceKey(baseId, effect.Kind));
                }
            }
        }

        // 정보 패널: 사거리 / 대상 1기당 DPS × 동시 대상 수 / 효과.
        public override string DescribeStats()
        {
            if (fields == null) return null;

            float dps = TickInterval > 0f ? DamagePerTick / TickInterval : 0f;
            return TowerStatsFormatter.Join(
                TowerStatsFormatter.BuildRangeLine(Range),
                TowerStatsFormatter.BuildBeamLine(dps, fields.MaxTargets),
                AttackAction.DescribeEffects(effects, Owner));
        }

        // ── 빔 비주얼(최소 구현) ──────────────────────────────────────────────
        void EnsureBeamPool()
        {
            if (beams != null) return;

            beams = new List<LineRenderer>(Mathf.Max(1, fields?.MaxTargets ?? 1));
            beamMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.25f, 0.1f, 0.9f) };
        }

        LineRenderer CreateBeam()
        {
            var go = new GameObject("BeamVisual");
            go.transform.SetParent(Owner.transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.widthMultiplier = 0.5f;   // 임시 연출값 — 콜라이더 반경 5 안팎의 월드 스케일에 맞춰 두껍게
            lr.material = beamMaterial;
            lr.enabled = false;
            return lr;
        }

        // 잠긴 대상 수만큼 빔을 켜고 위치를 따라가게 한다. 다음 틱 전에 죽거나 사라진 대상은 그 빔만 끈다.
        void FollowLockedTargets()
        {
            EnsureBeamPool();
            while (beams.Count < lockedTargets.Count) beams.Add(CreateBeam());

            // 빔이 **나가는 곳**은 포신(firePoint)이다 — AttackAction이 투사체를 그 지점에서
            // 생성하는 것과 같은 규칙이며, 미할당 프리팹은 타워 루트로 폴백하는 것도 동일하다.
            // ⚠ `MaintainLocks`의 사거리 판정 원점은 **일부러 루트(`Origin`)로 남겨 둔다** — 포신
            // 높이에서 구형 판정을 하면 지상 적 기준 수평 도달거리가 그 높이만큼 줄고, 바닥에
            // 그리는 사거리 원(`DrawGizmos`/`DisplayRange`)과 실제 도달 범위가 어긋난다.
            // 즉 여기는 연출, 저기는 규칙이라 원점이 갈리는 것이 의도다.
            Transform firePoint = Owner.FirePoint;
            Vector3 origin = firePoint != null ? firePoint.position : Origin.position;

            for (int i = 0; i < beams.Count; i++)
            {
                IDamageable victim = i < lockedTargets.Count ? lockedTargets[i] : null;
                if (victim == null || victim.IsDead || victim.HitPosition == null)
                {
                    beams[i].enabled = false;
                    continue;
                }

                beams[i].enabled = true;
                beams[i].SetPosition(0, origin);
                beams[i].SetPosition(1, victim.HitPosition.position);
            }
        }

        void HideAllBeams()
        {
            if (beams == null) return;
            for (int i = 0; i < beams.Count; i++) beams[i].enabled = false;
        }

#if UNITY_EDITOR
        public override void DrawGizmos()
        {
            if (fields == null) return;
            UnityEditor.Handles.color = new Color(1f, 0.25f, 0.1f);
            UnityEditor.Handles.DrawWireDisc(Origin.position, Vector3.up, Range);
        }
#endif
    }
}
