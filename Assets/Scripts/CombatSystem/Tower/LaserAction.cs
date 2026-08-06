using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 조준 방향으로 **사각 레이저를 즉시 뿜어 그 범위의 적 전원을 동시에** 때리는 액션(#300).
    // 투사체가 없고(히트스캔) 대상을 지속 잠그지도 않으므로 `AttackAction`·`BeamAction` 어느 쪽도 아니다.
    //
    // ★ **충전을 이 액션이 직접 소유한다.** 쏘지 않는 동안 단계가 쌓여 ① 레이저 폭이 넓어지고
    // ② 공격력이 스택당 절댓값만큼 오르며, 발사하는 순간 0으로 풀린다.
    //
    // 왜 원장형 램프(`RampAction`)를 쓰지 않는가 — **원장에 "폭" 축이 없다.** `TowerStat`은
    // AttackDamage/AttackRange/AttackSpeed 3축이고 `CombatSpace.TileBuffStat`과 1:1 대응이라 축을
    // 늘리면 타일 버프까지 번진다(같은 문제가 WL-127로 이미 열려 있다 — 슬로우 강도를 넣을 축이 없다).
    // `BeamAction`의 대상별 램프(`Beam.LockRamp`)가 같은 이유로 원장을 안 타는 것과 같은 판단이다.
    //
    // 그리고 이 결정이 **최대 함정을 제거한다**: 충전을 `RampAction`에 두면 리셋 신호가
    // `Tower.OnFired`라 이 액션이 그것을 직접 발행해야 하고, 잊으면 **경고도 예외도 없이 스택이
    // 영영 안 풀린다.** 자기가 소유하면 발사와 리셋이 같은 코드 경로라 잊을 수가 없다.
    [Serializable]
    public sealed class LaserAction : TowerAction
    {
        // ── 런타임 상태 (직렬화 금지 — TowerAction 규칙 ③) ──────────────────
        [NonSerialized] TowerAsset.LaserFields fields;
        [NonSerialized] List<HitEffect> effects;
        [NonSerialized] LayerMask enemyLayerMask;

        [NonSerialized] float cooldownTimer;

        // 마지막 발사 이후 경과 시간 = 충전 시계. 발사 시 0으로 되돌린다.
        [NonSerialized] float chargeTimer;

        [NonSerialized] Collider[] hitBuffer;

        // 한 발이 같은 적을 두 번 때리지 않게 한다 — 적 하나가 콜라이더를 여러 개 가질 수 있다.
        // (`AttackAction`은 대상을 하나만 고르고 `BeamAction`은 잠금 목록으로 걸러 이 문제가 없다.)
        [NonSerialized] HashSet<IDamageable> hitOnce;

        // 섬광(최소 구현). 연출 폴리싱은 별도 이슈 — 여기서는 판정 폭과 어긋나지 않는 것만 보장한다.
        [NonSerialized] LineRenderer flash;
        [NonSerialized] Material flashMaterial;
        [NonSerialized] float flashRemaining;

        // 섬광 유지 시간. 밸런스가 아니라 연출값이라 SO로 올리지 않는다(BeamAction의 빔 두께와 같은 축).
        const float FlashSeconds = 0.12f;

        // 낮에 충전이 진행되면 매 웨이브를 만충으로 시작한다 — 전투 행위이므로 밤에만 돈다.
        public override TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        public override float DisplayRange => Range;

        // 최종 스탯 = SO 기본값 + 원장 합성. 사거리·발사 주기는 다른 액션과 같은 축을 그대로 쓴다.
        public float Range =>
            fields == null ? 0f : Owner.Stats.Evaluate(TowerStat.AttackRange, fields.Range);

        public float Interval =>
            fields == null
                ? 0f
                : fields.Interval / Mathf.Max(Owner.Stats.Evaluate(TowerStat.AttackSpeed, 1f), 0.01f);

        /// 현재 충전 단계. `ChargeRamp` 미저작이면 항상 0(= 평범한 즉시 레이저).
        public int ChargeStacks
        {
            get
            {
                RampProfile ramp = fields?.ChargeRamp;
                return ramp == null || !ramp.IsAuthored ? 0 : ramp.StacksFromTime(chargeTimer);
            }
        }

        /// 이번에 쏜다면 나갈 레이저 폭. **판정과 섬광이 이 값 하나를 공유한다** —
        /// 따로 계산하면 보이는 레이저와 맞는 범위가 어긋난다(WL-079/WL-130과 같은 유형).
        public float WidthAt(int stacks)
        {
            if (fields == null) return 0f;

            RampProfile ramp = fields.ChargeRamp;
            float multiplier = ramp != null && ramp.IsAuthored ? ramp.Multiplier(stacks) : 1f;
            return fields.Width * multiplier;
        }

        /// 이번에 쏜다면 들어갈 피해.
        ///
        /// ⚠ 충전분은 **배율이 아니라 절댓값**으로 기본값에 더해 넘긴다 — 배율이면 기여가
        /// `기본값 × 스택 × 계수`라 나중에 기본 공격력을 조정할 때 충전 보상까지 함께 움직인다.
        /// 원장에 새 소스를 만들지 않는 이유: `Evaluate`가 기본값을 인자로 받으므로 여기 더하면 끝이다.
        /// (그 대신 충전분도 타일 버프·오라 배율에 함께 증폭된다 — 의도된 것이고 상한 산정의 기준이다.)
        public float DamageAt(int stacks)
        {
            if (fields == null) return 0f;

            float charged = fields.Damage + fields.DamagePerStack * stacks;
            return Owner.Stats.Evaluate(TowerStat.AttackDamage, charged);
        }

        // 저작이 갖춰졌는가. 미저작이면 Tick이 첫 줄에서 빠져나가 물리 예산을 쓰지 않는다
        // (`AttackAction.canFire`와 같은 축 — 무동작이면 비용도 0).
        bool Authored => fields != null && fields.Damage > 0f && fields.Width > 0f && fields.Range > 0f;

        protected override void OnInitialize(TowerAsset asset)
        {
            fields = asset.Laser;
            effects = asset.Effects;
            enemyLayerMask = Owner.EnemyLayerMask;

            cooldownTimer = 0f;
            chargeTimer = 0f;

            hitBuffer ??= new Collider[64];
            hitOnce ??= new HashSet<IDamageable>();

            HideFlash();
        }

        public override void Dispose()
        {
            // 외부에 남기는 상태가 없다(피해는 즉시 적용되고, 효과는 대상이 Duration으로 소진한다).
            cooldownTimer = 0f;
            chargeTimer = 0f;
            HideFlash();
        }

        /// 웨이브가 끝나면 섬광을 끄고 충전을 버린다.
        ///
        /// ⚠ **섬광 정리가 이 훅의 존재 이유다.** 이 액션은 `NightOnly`라 낮에는 `Tick`이 아예 돌지
        /// 않아 밤 마지막 프레임의 `LineRenderer.enabled = true`가 그대로 굳는다 — `BeamAction`이
        /// 정확히 그 문제로 낮에도 빔이 남아 있었다(#298 → #300에서 해소). 처음부터 막아 둔다.
        public override void OnWaveEnd()
        {
            cooldownTimer = 0f;
            chargeTimer = 0f;
            HideFlash();
        }

        public override void Tick(float deltaTime)
        {
            if (!Authored) return;

            // 섬광은 발사와 무관하게 스스로 사그라든다.
            if (flashRemaining > 0f)
            {
                flashRemaining -= deltaTime;
                if (flashRemaining <= 0f) HideFlash();
            }

            // 충전은 쿨다운과 별개로 계속 쌓인다 — "쿨타임이 돈 뒤부터"는 `StackInterval` ≥ `Interval`
            // 저작 규칙으로 표현한다(AttackAction의 private 쿨다운을 들여다보지 않기 위해서다).
            chargeTimer += deltaTime;

            cooldownTimer -= deltaTime;
            if (cooldownTimer > 0f) return;

            IDamageable target = FindTarget();
            if (target == null) return;   // 대상이 없으면 계속 충전한다

            Fire(target);
            cooldownTimer = Interval;
        }

        void Fire(IDamageable target)
        {
            // ⚠ 판정 원점은 **타워 루트**다(`BeamAction.MaintainLocks`와 같은 규칙). 포신 높이에서
            // 박스를 만들면 지상 적 기준 도달 범위가 그 높이만큼 어긋나고, 바닥에 그리는 사거리 원과
            // 실제 범위가 달라진다. 포신은 섬광이 나가는 곳으로만 쓴다.
            Vector3 origin = Origin.position;

            Transform aimAt = target.HitPosition;
            Vector3 dir = aimAt != null ? aimAt.position - origin : Origin.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            // 폭·피해가 **같은 스택 수**에서 나오게 한 번만 읽는다.
            int stacks = ChargeStacks;
            float width = WidthAt(stacks);
            float damage = DamageAt(stacks);
            float range = Range;

            ApplyBox(origin, dir, width, range, damage);
            ShowFlash(dir, width, range);

            // 발사와 리셋이 같은 경로에 있다 — 이것이 충전을 이 액션이 소유하는 이유다.
            chargeTimer = 0f;

            // 탄약 연출 등 구독자 통지(AttackAction과 같은 창구). 발사 1회당 1번.
            Owner.RaiseFired();
        }

        void ApplyBox(Vector3 origin, Vector3 dir, float width, float range, float damage)
        {
            // 박스는 지면에서 Height만큼 올라간 직육면체다 — 세로를 넉넉히 주면 발사 높이와 무관해져
            // 고정 방향 투사체의 "발사 높이가 곧 평생 높이" 함정(#298)이 애초에 생기지 않는다.
            float height = Mathf.Max(fields.Height, 0.1f);
            Vector3 center = origin + dir * (range * 0.5f) + Vector3.up * (height * 0.5f);
            Vector3 halfExtents = new Vector3(width * 0.5f, height * 0.5f, range * 0.5f);
            Quaternion orientation = Quaternion.LookRotation(dir);

            int count = Physics.OverlapBoxNonAlloc(
                center, halfExtents, hitBuffer, orientation, enemyLayerMask);

            hitOnce.Clear();
            int baseId = Owner.GetInstanceID();
            TowerStats stats = Owner.Stats;

            for (int i = 0; i < count; i++)
            {
                IDamageable victim = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (victim == null || victim.IsDead || victim.Faction == Owner.Faction) continue;
                if (!hitOnce.Add(victim)) continue;   // 적 하나가 콜라이더 여러 개를 가질 수 있다

                victim.TakeDamage(new DamageInfo(damage, Owner));

                // 히트스캔은 `Projectile.DamageDealt`를 발행하지 않아 투사체 기반 보상(#169 버프 화상)에서
                // 자동 제외되는 것이 기본 축이다(TowerAddGuide.md §6). 이 타워는 충전 한 방이 정체성이라
                // "한 방에 화상까지"가 결이 맞고, 보상을 골랐는데 특정 타워만 반응이 없는 것도 설명하기
                // 어려우므로 **같은 문서가 적어둔 탈출구대로 직접 발행해 축에 포함시킨다.**
                Projectile.RaiseDamageDealt(Owner, victim);

                if (effects == null) continue;
                for (int e = 0; e < effects.Count; e++)
                {
                    HitEffect effect = effects[e];
                    if (effect == null) continue;
                    if (!Owner.IsEffectActive(effect.Kind)) continue;   // 합성 계승 필터(다른 액션과 동일)

                    effect.Apply(victim, Owner, stats, HitEffect.SourceKey(baseId, effect.Kind));
                }
            }
        }

        // 사거리 내 가장 가까운 적 — 조준 방향을 정하기 위한 것뿐이다. 실제 피해는 박스가 정한다.
        IDamageable FindTarget()
        {
            Vector3 origin = Origin.position;
            int count = Physics.OverlapSphereNonAlloc(origin, Range, hitBuffer, enemyLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider hit = hitBuffer[i];
                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable == null || damageable.IsDead || damageable.Faction == Owner.Faction) continue;

                float sqrDistance = (hit.transform.position - origin).sqrMagnitude;
                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closest = damageable;
                }
            }
            return closest;
        }

        // 정보 패널: 사거리 / 피해·폭의 미충전~만충 구간 / 발사 주기 / 효과.
        // 현재값만 내면 "충전하면 강해진다"는 정체성의 절반이 표시에서 사라진다(패널은 스냅샷이다).
        public override string DescribeStats()
        {
            if (!Authored) return null;

            RampProfile ramp = fields.ChargeRamp;
            int max = ramp != null && ramp.IsAuthored ? ramp.MaxStacks : 0;

            string damageLine = max > 0
                ? $"{LabelDamage}: {DamageAt(0):0.#} → {DamageAt(max):0.#}"
                : $"{LabelDamage}: {DamageAt(0):0.#}";

            string widthLine = max > 0
                ? $"Laser width: {WidthAt(0):0.#} → {WidthAt(max):0.#} ({ramp.StackInterval * max:0.#}s)"
                : $"Laser width: {WidthAt(0):0.#}";

            return TowerStatsFormatter.Join(
                damageLine,
                TowerStatsFormatter.BuildRangeLine(Range),
                widthLine,
                AttackAction.DescribeEffects(effects, Owner));
        }

        // 공격력 라벨은 공격 타워와 같은 것을 쓴다 — 같은 개념에 다른 이름을 내면 플레이어가 못 잇는다.
        static string LabelDamage => LocalizationHelper.Get(
            LocalizationHelper.k_DefaultTable, "game.tower.attack_damage");

        // ── 섬광(최소 구현) ────────────────────────────────────────────────
        void ShowFlash(Vector3 dir, float width, float range)
        {
            EnsureFlash();
            if (flash == null) return;

            // 섬광은 **포신에서** 나간다(투사체 생성 지점과 같은 규칙, 미할당이면 루트 폴백).
            Transform firePoint = Owner.FirePoint;
            Vector3 from = firePoint != null ? firePoint.position : Origin.position;

            flash.widthMultiplier = width;   // 판정 폭과 같은 값 — 어긋나면 안 된다
            flash.SetPosition(0, from);
            flash.SetPosition(1, from + dir * range);
            flash.enabled = true;
            flashRemaining = FlashSeconds;
        }

        void EnsureFlash()
        {
            if (flash != null) return;

            flashMaterial ??= new Material(Shader.Find("Sprites/Default"))
            {
                color = new Color(0.4f, 0.9f, 1f, 0.85f),
            };

            var go = new GameObject("LaserFlash");
            go.transform.SetParent(Owner.transform, false);

            flash = go.AddComponent<LineRenderer>();
            flash.positionCount = 2;
            flash.material = flashMaterial;
            flash.enabled = false;
        }

        void HideFlash()
        {
            flashRemaining = 0f;
            if (flash != null) flash.enabled = false;
        }

#if UNITY_EDITOR
        public override void DrawGizmos()
        {
            if (fields == null) return;

            UnityEditor.Handles.color = new Color(0.4f, 0.9f, 1f);
            UnityEditor.Handles.DrawWireDisc(Origin.position, Vector3.up, Range);

            // 만충 폭을 기즈모로 보여준다 — 배치 시 "이 길목을 얼마나 덮는가"가 이 타워의 판단 근거다.
            RampProfile ramp = fields.ChargeRamp;
            int max = ramp != null && ramp.IsAuthored ? ramp.MaxStacks : 0;
            float width = WidthAt(max);
            if (width <= 0f) return;

            Vector3 dir = Origin.forward;
            Vector3 center = Origin.position + dir * (Range * 0.5f);
            UnityEditor.Handles.matrix = Matrix4x4.TRS(center, Quaternion.LookRotation(dir), Vector3.one);
            UnityEditor.Handles.DrawWireCube(Vector3.zero, new Vector3(width, 0.1f, Range));
            UnityEditor.Handles.matrix = Matrix4x4.identity;
        }
#endif
    }
}
