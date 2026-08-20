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
    // 대상별 유지 시간 램프(#300)를 선택적으로 쓴다 — `BeamFields.LockRamp`를 저작하면 같은 대상을
    // 계속 잠근 시간만큼 피해가 오르고, **미저작이면 기존처럼 균일 계수**다(거동 무변경).
    // `MaxTargets=1` + `LockRamp` = 단일 인페르노, `MaxTargets=N` + 램프 없음 = 멀티 인페르노 —
    // 두 타워가 같은 액션에서 SO 저작만으로 갈린다.
    //
    // ⚠ 이 램프는 `RampAction`(원장형)과 **적용 범위가 다르다.** 원장은 타워 단위라 대상이 바뀌어도
    // 배율이 남고 다른 액션·DoT까지 강해진다 — 그래서 여기서는 원장을 쓰지 않고 대상별로 직접 곱한다.
    // 공유하는 것은 수치 규약(`RampProfile`)뿐이다.
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

        // 대상별 잠금 경과 시간(#300). `lockedTargets`와 **인덱스가 나란히 유지되는** 병렬 리스트다 —
        // 잠금이 sticky해서 목록 순서가 안정적이므로 Dictionary 없이 인덱스로 짝지을 수 있고,
        // 매 프레임 도는 경로라 딕셔너리 조회·할당을 피하는 편이 낫다.
        // ⚠ `lockedTargets`를 넣거나 빼는 모든 지점에서 이 리스트도 같은 인덱스로 함께 갱신할 것.
        [NonSerialized] List<float> lockElapsed;

        // 빔 비주얼 — 최소 구현(임시 머티리얼, LineRenderer). 프리팹에 `TowerBeamVisual`이 배선돼 있으면
        // 그쪽에 그리기를 넘기고 이 폴백은 아예 만들지 않는다.
        [NonSerialized] List<LineRenderer> beams;
        [NonSerialized] Material beamMaterial;

        // 단계형 빔 연출(있으면). 연출 수치는 액션이 갖지 않는다는 규칙 ①에 따라 프리팹 컴포넌트가 소유한다.
        [NonSerialized] TowerBeamVisual visual;

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
            lockElapsed ??= new List<float>();

            visual = Owner.BeamVisual;
            if (visual == null) EnsureBeamPool();   // 폴백만 준비한다 — 둘을 함께 만들면 빔이 두 겹으로 보인다
        }

        public override void Dispose()
        {
            // 잠금 목록은 인스턴스 소유라 여기서 비우면 재활성화 시 새로 잠근다 — 걸린 효과는 대상 쪽
            // Duration으로 스스로 소진된다(디버프 오라와 같은 설계). 빔 시각 오브젝트는 재사용하도록
            // 숨기기만 한다.
            tickTimer = 0f;
            lockedTargets?.Clear();
            lockElapsed?.Clear();
            HideAllBeams();
        }

        /// 웨이브가 끝나면 잠금과 그 경과 시간을 버리고 빔을 끈다.
        ///
        /// ⚠ **이 훅이 없으면 빔이 낮에도 켜진 채로 남는다.** 이 액션은 `NightOnly`라 낮에는 `Tick`이
        /// 아예 돌지 않고, 그래서 `MaintainLocks`(잠금 정리)도 `FollowLockedTargets`(빔 위치·표시 갱신)도
        /// 멈춘다 — 밤 마지막 프레임의 `LineRenderer.enabled = true`가 그대로 굳는다. 대상이 이미
        /// 파괴됐어도 스스로 지워질 경로가 없다(#300 검증 중 실측: 낮 내내 잠금 1기와 경과 19초가 잔존).
        ///
        /// 램프 관점에서도 맞다 — 성장은 웨이브를 넘기지 않는다는 규칙을 원장형 램프와 공유한다.
        public override void OnWaveEnd()
        {
            lockedTargets?.Clear();
            lockElapsed?.Clear();
            HideAllBeams();
            tickTimer = 0f;   // 다음 밤 첫 Tick에서 즉시 1회 적용
        }

        public override void Tick(float deltaTime)
        {
            if (fields == null || fields.MaxTargets <= 0 || fields.DamagePerTick <= 0f) return;

            // 잠금 유지·보충은 매 프레임 — "죽거나 사거리를 벗어나야만 교체"를 지키려면 빈 슬롯이
            // 생기자마자(다음 틱까지 기다리지 않고) 채워야 한다. 데미지 적용만 TickInterval을 따른다.
            MaintainLocks();

            // 잠금 경과 시간은 **매 프레임** 누적한다 — 램프는 "조준을 유지한 시간"이라 피해 틱 주기와
            // 무관해야 한다. 틱마다 세면 공속 버프가 붙었을 때 같은 실시간에 램프가 더 빨리 오른다.
            AccumulateLockTime(deltaTime);

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
                if (v == null || v.IsDead || outOfRange)
                {
                    lockedTargets.RemoveAt(i);

                    // 경과 시간도 함께 버린다 — 사거리를 벗어났다 돌아온 적은 0에서 다시 시작한다.
                    // 램프를 "유지한 시간"으로 정의했으므로 잠금이 끊기면 그 시간도 끊긴다.
                    lockElapsed.RemoveAt(i);
                }
            }

            if (lockedTargets.Count >= fields.MaxTargets) return;   // 빈 슬롯 없으면 재탐색 불필요

            int count = Physics.OverlapSphereNonAlloc(origin, Range, hitBuffer, enemyLayerMask);
            for (int i = 0; i < count && lockedTargets.Count < fields.MaxTargets; i++)
            {
                IDamageable d = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (d == null || d.IsDead || d.Faction == Owner.Faction) continue;
                if (lockedTargets.Contains(d)) continue;   // 이미 잠근 대상 중복 추가 방지
                lockedTargets.Add(d);
                lockElapsed.Add(0f);                      // 새 대상은 램프 0부터
            }
        }

        // 잠금 유지 시간 누적. `lockedTargets`와 인덱스가 나란하므로 길이가 어긋날 일이 없지만,
        // 병렬 리스트라 방어적으로 짧은 쪽까지만 돈다.
        void AccumulateLockTime(float deltaTime)
        {
            int count = lockedTargets.Count < lockElapsed.Count ? lockedTargets.Count : lockElapsed.Count;
            for (int i = 0; i < count; i++) lockElapsed[i] += deltaTime;
        }

        // 이 대상에게 적용할 램프 배율. LockRamp 미저작이면 1(균일 지속딜 = 기존 거동).
        float LockMultiplier(int index)
        {
            RampProfile ramp = fields?.LockRamp;
            if (ramp == null || !ramp.IsAuthored) return 1f;
            if (index < 0 || index >= lockElapsed.Count) return 1f;

            return ramp.Multiplier(ramp.StacksFromTime(lockElapsed[index]));
        }

        /// 연출용 램프 진행도(0~1) = 달성 스택 ÷ MaxStacks. **배율이 아니라 진행도를 넘기는 이유**는
        /// 연출이 밸런스 수치에 묶이지 않게 하기 위함이다 — `PerStack`을 조정해 최대 배율이 ×13에서
        /// ×20으로 바뀌어도 단계 문턱을 다시 잡을 필요가 없다.
        ///
        /// 램프 미저작(멀티 인페르노)은 0을 돌려 항상 첫 단계로 그려진다 — 균일 지속딜이라 그게 맞다.
        float RampProgress(int index)
        {
            RampProfile ramp = fields?.LockRamp;
            if (ramp == null || !ramp.IsAuthored || ramp.MaxStacks <= 0) return 0f;
            if (index < 0 || index >= lockElapsed.Count) return 0f;

            return Mathf.Clamp01(ramp.StacksFromTime(lockElapsed[index]) / (float)ramp.MaxStacks);
        }

        void ApplyToLocked()
        {
            int baseId = Owner.GetInstanceID();
            TowerStats stats = Owner.Stats;
            float damage = DamagePerTick;

            for (int i = 0; i < lockedTargets.Count; i++)
            {
                IDamageable victim = lockedTargets[i];

                // 램프는 **원장을 통과하지 않고 여기서만 곱해진다.** 원장에 넣으면 타워 단위가 되어
                // 대상이 바뀌어도 유지되고 아래 효과(DoT)까지 함께 강해진다 — 그래서 효과 수치는
                // 의도적으로 램프를 타지 않는다(효과는 `Owner.Stats`만 본다).
                victim.TakeDamage(new DamageInfo(damage * LockMultiplier(i), Owner));

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

        // 정보 패널: 사거리 / 대상 1기당 DPS × 동시 대상 수 / (램프가 있으면) 성장 구간 / 효과.
        public override string DescribeStats()
        {
            if (fields == null) return null;

            float dps = TickInterval > 0f ? DamagePerTick / TickInterval : 0f;

            // 램프가 있으면 최대 DPS를 함께 낸다 — 시작 DPS만 보여주면 단일 인페르노가 그냥 약한
            // 타워로 읽힌다(정체성이 "오래 지지면 아프다"인데 그 절반이 표시에서 사라진다).
            RampProfile ramp = fields.LockRamp;
            string rampLine = ramp != null && ramp.IsAuthored
                ? $"Lock ramp: ×1 → ×{ramp.Multiplier(ramp.MaxStacks):0.##} ({ramp.StackInterval * ramp.MaxStacks:0.#}s)"
                : null;

            return TowerStatsFormatter.Join(
                TowerStatsFormatter.BuildRangeLine(Range),
                TowerStatsFormatter.BuildBeamLine(dps, fields.MaxTargets),
                rampLine,
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
            // 폴백 풀은 연출 컴포넌트가 없을 때만 만든다 — 아래 분기보다 앞에서 만들면
            // `TowerBeamVisual`이 배선된 타워에도 쓰지 않는 LineRenderer가 생긴다.
            if (visual == null)
            {
                EnsureBeamPool();
                while (beams.Count < lockedTargets.Count) beams.Add(CreateBeam());
            }

            // 빔이 **나가는 곳**은 포신(firePoint)이다 — AttackAction이 투사체를 그 지점에서
            // 생성하는 것과 같은 규칙이며, 미할당 프리팹은 타워 루트로 폴백하는 것도 동일하다.
            // ⚠ `MaintainLocks`의 사거리 판정 원점은 **일부러 루트(`Origin`)로 남겨 둔다** — 포신
            // 높이에서 구형 판정을 하면 지상 적 기준 수평 도달거리가 그 높이만큼 줄고, 바닥에
            // 그리는 사거리 원(`DrawGizmos`/`DisplayRange`)과 실제 도달 범위가 어긋난다.
            // 즉 여기는 연출, 저기는 규칙이라 원점이 갈리는 것이 의도다.
            Transform firePoint = Owner.FirePoint;
            Vector3 origin = firePoint != null ? firePoint.position : Origin.position;

            // 단계형 연출이 배선돼 있으면 그쪽이 굵기·색까지 책임진다. 램프 진행도를 함께 넘겨
            // "무엇을 그릴지"의 판단을 연출 쪽에 남긴다 — 액션은 진행도만 계산한다.
            if (visual != null)
            {
                for (int i = 0; i < lockedTargets.Count; i++)
                {
                    IDamageable v = lockedTargets[i];
                    if (v == null || v.IsDead || v.HitPosition == null) continue;
                    visual.Draw(i, origin, v.HitPosition.position, RampProgress(i));
                }
                visual.HideFrom(lockedTargets.Count);
                return;
            }

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
            visual?.HideAll();

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
