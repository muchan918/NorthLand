using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 차징 캐논(#300 설계 → #311 구현). 쏘지 않는 동안 충전되고, 쏘는 순간 풀린다.
    // 발사는 유도탄이 아니라 **조준 방향으로 사각 레이저를 즉시 뿜어 그 범위의 적 전원을 동시에** 때린다.
    //
    // ⚠ **왜 사각 판정인가.** 차징만 있으면 "적이 드문 자리에 놓을수록 강한 타워"가 된다 — 사거리에 적이
    // 들어오면 즉시 쏘므로 플레이어가 참을 수 없고, 번잡한 길목에 놓으면 충전이 0이다. 범위 동시타를
    // 붙이면 정체성이 "긴 직선 길목을 조준해 줄 서 있는 무리를 한 번에 쓸어버리는 배치 퍼즐"이 된다.
    //
    // ⚠ **충전을 `RampAction`(원장형)이 아니라 이 액션이 소유한다.** 폭이 충전에 따라 변하는데
    // `TowerStat`은 AttackDamage/AttackRange/AttackSpeed 3축뿐이고 `CombatSpace.TileBuffStat`과 1:1
    // 대응이라, "폭" 축을 늘리면 타일 버프까지 번진다(같은 문제가 WL-127로 이미 열려 있다). 원장이
    // 표현 못 하는 값을 액션이 직접 굴리는 것은 `BeamFields.LockRamp`가 만든 선례 그대로다.
    //
    // 이 배치가 최대 함정을 제거한다 — 원장형이었다면 리셋 신호를 이 액션이 밖으로 발행해야 했고,
    // **잊으면 경고도 예외도 없이 스택이 영영 안 풀렸다.** 자기 충전을 소유하면 발사와 리셋이 같은
    // 코드 경로(`Fire`)라 잊을 수가 없다.
    [Serializable]
    public sealed class LaserAction : TowerAction
    {
        // ── 런타임 상태 (직렬화 금지 — TowerAction 규칙 ③) ──────────────────
        [NonSerialized] TowerAsset.LaserFields fields;
        [NonSerialized] List<HitEffect> effects;
        [NonSerialized] LayerMask enemyLayerMask;

        [NonSerialized] float cooldownTimer;

        // 마지막 발사 이후 경과 시간 = 충전량. 스택은 이 값에서 파생되므로 따로 세지 않는다
        // (스택 카운터를 별도로 두면 리셋 지점이 둘이 되어 한쪽만 빠뜨릴 수 있다).
        [NonSerialized] float chargeElapsed;

        [NonSerialized] Collider[] hitBuffer;

        // 이 타워가 애초에 쏠 수 있는가. 조립 시 1회 판정한다 — 판정 재료가 전부 SO 값이라
        // Initialize 사이에 바뀌지 않는다. **무동작이면 비용도 0**이어야 한다(AttackAction.canFire와 같은 축).
        [NonSerialized] bool canFire;

        // 섬광(최소 구현). 연출 폴리싱은 별도 이슈 — #298·#300과 같은 기준.
        [NonSerialized] LineRenderer flash;
        [NonSerialized] Material flashMaterial;
        [NonSerialized] float flashTimer;

        public override TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        public float Range =>
            fields == null ? 0f : Owner.Stats.Evaluate(TowerStat.AttackRange, fields.Range);

        // 공격속도는 배율 스탯이라 기본값 1f로 평가한다(AttackAction.Interval과 같은 규칙).
        public float Interval =>
            fields == null
                ? 0f
                : fields.Interval / Mathf.Max(Owner.Stats.Evaluate(TowerStat.AttackSpeed, 1f), 0.01f);

        /// 현재 충전 단계. 경과 시간에서 파생된다 — `RampProfile`을 시간 기반으로 쓰는 세 번째 소비처다
        /// (`RampAction`=이벤트, `BeamFields.LockRamp`=대상별 유지 시간, 여기=마지막 발사 이후 시간).
        public int ChargeStacks
        {
            get
            {
                RampProfile ramp = fields?.ChargeRamp;
                return ramp == null ? 0 : ramp.StacksFromTime(chargeElapsed);
            }
        }

        /// 스택 수에 대응하는 판정 폭. **섬광도 반드시 이 값을 쓴다** — 따로 계산하면 보이는 레이저와
        /// 맞는 범위가 어긋난다(WL-079/WL-130이 지적한 "표시부와 적용부가 각자 규칙을 씀" 유형).
        public float WidthAt(int stacks)
        {
            if (fields == null) return 0f;

            RampProfile ramp = fields.ChargeRamp;
            float multiplier = ramp != null && ramp.IsAuthored ? ramp.Multiplier(stacks) : 1f;
            return fields.Width * multiplier;
        }

        /// 스택 수에 대응하는 최종 공격력.
        ///
        /// ⚠ 충전분은 **배율이 아니라 스택당 절댓값(flat)**이고, 원장에 새 소스를 만들지 않는다 —
        /// `Evaluate(stat, baseValue)`가 기본값을 인자로 받으므로 거기 더해 넘기면 끝이다.
        /// flat인 이유는 지수 증가 방지가 아니다(원장 배율은 어차피 보너스 합산이다). **기본 공격력과
        /// 분리되기 때문**이다 — 배율이면 충전 기여가 `기본값 × 스택 × 계수`라 나중에 기본값을 조정하면
        /// 충전 보상까지 함께 움직이는데, flat이면 두 값을 독립적으로 밸런싱할 수 있다.
        public float DamageAt(int stacks)
            => fields == null
                ? 0f
                : Owner.Stats.Evaluate(TowerStat.AttackDamage, fields.Damage + fields.DamagePerStack * stacks);

        public override float DisplayRange => Range;

        protected override void OnInitialize(TowerAsset asset)
        {
            fields = asset.Laser;
            effects = asset.Effects;
            enemyLayerMask = Owner.EnemyLayerMask;

            cooldownTimer = 0f;
            chargeElapsed = 0f;
            flashTimer = 0f;

            canFire = fields != null
                      && fields.Damage > 0f
                      && fields.Range > 0f
                      && fields.Width > 0f
                      && fields.Height > 0f;

            hitBuffer ??= new Collider[32];
        }

        public override void Dispose()
        {
            // 외부에 남기는 상태가 없다(피해는 즉시 적용되고 효과는 대상이 Duration을 소진한다).
            // 충전은 인스턴스 소유라 여기서 비우면 재활성화 시 0에서 다시 찬다 — 합성 롤백
            // (Release/Reoccupy = OnDisable/OnEnable 왕복)에서 만충이 살아남지 않는 것이 의도다.
            cooldownTimer = 0f;
            chargeElapsed = 0f;
            HideFlash();
        }

        /// 웨이브가 끝나면 충전을 버리고 섬광을 끈다.
        ///
        /// ⚠ **섬광 끄기가 이 훅의 핵심이다.** 이 액션은 `NightOnly`라 낮에는 `Tick`이 아예 돌지 않아
        /// `flashTimer`가 줄지 않는다 — 밤 마지막 프레임에 켜진 `LineRenderer.enabled = true`가 낮 내내
        /// 그대로 굳는다. `BeamAction`이 정확히 이 문제를 갖고 있었다(#298 → #300에서 해소).
        ///
        /// 충전 초기화는 "성장은 웨이브를 넘기지 않는다"는 #300 확정 규칙을 따르는 것이다.
        public override void OnWaveEnd()
        {
            cooldownTimer = 0f;   // 다음 밤 첫 대상에 즉시 사격
            chargeElapsed = 0f;
            HideFlash();
        }

        public override void Tick(float deltaTime)
        {
            // 저작이 비어 있으면 매 프레임 OverlapSphere를 태우지 않는다(AttackAction.Tick과 같은 이유).
            if (!canFire) return;

            TickFlash(deltaTime);

            // 충전은 쿨다운과 **독립적으로** 흐른다. 둘을 묶으면 "쿨타임이 끝난 뒤부터 충전"을
            // 표현할 수 없는데, 그 관계는 저작 규칙(`StackInterval` ≥ `Interval`)으로 표현한다 —
            // `AttackAction.cooldownTimer`처럼 다른 액션의 내부를 들여다보는 결합을 만들지 않는다.
            chargeElapsed += deltaTime;

            cooldownTimer -= deltaTime;
            if (cooldownTimer > 0f) return;

            IDamageable target = FindTarget();
            if (target == null) return;

            if (Fire(target)) cooldownTimer = Interval;
        }

        /// 조준 방향으로 사각 레이저를 1회 뿜는다. **리셋이 여기 있다** — 발사와 같은 코드 경로다.
        bool Fire(IDamageable target)
        {
            Vector3 origin = Origin.position;

            Transform aimAt = target?.HitPosition;
            if (aimAt == null) return false;

            Vector3 dir = aimAt.position - origin;
            dir.y = 0f;                                   // 판정은 수평 박스다 — 고저차는 Height가 흡수한다
            if (dir.sqrMagnitude < 0.0001f) return false;
            dir.Normalize();

            // 폭·피해는 **같은 스택 수 하나**에서 나온다. 발사 도중 스택이 바뀔 여지를 없애려고
            // 지역 변수로 스냅샷한 뒤 판정·섬광이 모두 그 값을 쓴다.
            int stacks = ChargeStacks;
            float width = WidthAt(stacks);
            float damage = DamageAt(stacks);
            float range = Range;

            ApplyBox(origin, dir, width, range, damage);
            ShowFlash(dir, width, range);

            // ⚠ **리셋은 판정 성공 여부와 무관하게 발사와 함께 일어난다.** "빗나가면 충전 유지"로 두면
            // 사거리에 적이 있는 한 계속 쏘면서 만충을 유지하는 무한 고화력 루프가 생긴다.
            chargeElapsed = 0f;

            Owner.RaiseFired();
            return true;
        }

        // 조준 방향으로 폭 × 높이 × 사거리 박스를 한 번 굴려 그 안 전원을 동시에 때린다.
        // 관통·연쇄가 아니라 **범위 동시타**라 대상 간 감쇠가 없다.
        void ApplyBox(Vector3 origin, Vector3 dir, float width, float range, float damage)
        {
            // 박스는 타워 앞으로 뻗으므로 중심이 사거리의 절반 지점이다. 세로는 지면에서 위로 Height —
            // ⚠ 몬스터가 부양해 있어(WL-063: 약 6f) 원점 높이에 딱 붙는 얇은 박스는 아무도 못 잡는다.
            Vector3 center = origin + dir * (range * 0.5f) + Vector3.up * (fields.Height * 0.5f);
            var halfExtents = new Vector3(width * 0.5f, fields.Height * 0.5f, range * 0.5f);
            Quaternion orientation = Quaternion.LookRotation(dir);

            int count = Physics.OverlapBoxNonAlloc(
                center, halfExtents, hitBuffer, orientation, enemyLayerMask);

            // ⚠ 버퍼가 넘치면 `OverlapBoxNonAlloc`은 예외 없이 초과분을 버린다 — 만충 시 폭이 넓어져
            // 실제로 닿을 수 있는 조합이라 에디터에서만 알린다(WL-157과 같은 축).
#if UNITY_EDITOR
            if (count >= hitBuffer.Length)
                Debug.LogWarning($"[LaserAction] {Owner.name}: 판정 버퍼({hitBuffer.Length})가 가득 찼습니다 " +
                                 "— 초과한 적은 조용히 피해를 받지 않습니다.", Owner);
#endif

            TowerStats stats = Owner.Stats;
            int baseId = Owner.GetInstanceID();

            for (int i = 0; i < count; i++)
            {
                IDamageable victim = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (victim == null || victim.IsDead || victim.Faction == Owner.Faction) continue;

                victim.TakeDamage(new DamageInfo(damage, Owner));

                // ⚠ **히트스캔은 투사체 보상(#169 버프 화상)에서 자동 제외된다** — 그 보상은
                // `Projectile.DamageDealt`를 구독하는데 여기는 `Projectile`을 만들지 않기 때문이다
                // (TowerAddGuide.md §6이 설계 의도로 명문화). 이 타워는 "충전 한 방"이 정체성이라
                // 화상이 결에 맞고, 플레이어가 고른 보상에 특정 타워만 조용히 반응이 없는 것도
                // 설명하기 어렵다 → 같은 문서가 적어둔 탈출구대로 직접 발행해 축에 포함시킨다.
                Projectile.RaiseDamageDealt(Owner, victim);

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

        // 사거리 내 가장 가까운 적. 이 타워는 그 적을 **조준 방향으로만** 쓰고 실제 피해는 박스가 정한다.
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

        // 정보 패널: 공격력(최소~만충) / 사거리 / 발사 간격 / 폭(최소~만충) / 효과.
        //
        // ⚠ 단일 값만 내면 이 타워가 그냥 약한 타워로 읽힌다 — 정체성의 절반이 "참으면 세진다"인데
        // 패널은 선택 시점 스냅샷이라 그 절반이 표시에서 사라진다(BeamAction의 램프 줄과 같은 판단).
        public override string DescribeStats()
        {
            if (fields == null) return null;

            RampProfile ramp = fields.ChargeRamp;
            bool charged = ramp != null && ramp.IsAuthored;
            int maxStacks = charged ? ramp.MaxStacks : 0;

            string damageLine = charged
                ? $"Damage: {DamageAt(0):0.#} → {DamageAt(maxStacks):0.#}"
                : $"Damage: {DamageAt(0):0.#}";

            string widthLine = charged
                ? $"Width: {WidthAt(0):0.#} → {WidthAt(maxStacks):0.#} ({ramp.StackInterval * maxStacks:0.#}s)"
                : $"Width: {WidthAt(0):0.#}";

            return TowerStatsFormatter.Join(
                damageLine,
                TowerStatsFormatter.BuildRangeLine(Range),
                $"Interval: {Interval:0.##}s",
                widthLine,
                AttackAction.DescribeEffects(effects, Owner));
        }

        // ── 섬광(최소 구현) ───────────────────────────────────────────────────
        // 폭은 판정과 **같은 값 하나**를 받는다 — 여기서 다시 계산하지 않는다.
        void ShowFlash(Vector3 dir, float width, float range)
        {
            EnsureFlash();

            // 섬광이 **나가는 곳**은 포신(firePoint)이다 — AttackAction이 투사체를 그 지점에서
            // 생성하고 BeamAction이 빔을 거기서 쏘는 것과 같은 규칙. 판정 원점은 일부러 루트로
            // 남겨 둔다(포신 높이에서 박스를 굴리면 바닥 사거리 원과 실제 도달 범위가 어긋난다).
            Transform firePoint = Owner.FirePoint;
            Vector3 from = firePoint != null ? firePoint.position : Origin.position;

            // ⚠ **끝점은 판정 박스의 세로 중심에 맞춘다.** firePoint에서 수평으로 뻗으면 포신이 높은
            // 프리팹(ChargingCannon은 y≈11.6)에서 빔이 몬스터 머리 위를 지나가 "판정은 맞는데 안 맞아
            // 보이는" 그림이 된다 — 몬스터는 지면에서 부양해 있다(WL-063: 약 6f).
            // 박스는 루트에서 위로 Height만큼 서므로 그 절반이 실제로 적이 잡히는 높이다.
            Vector3 to = Origin.position + dir * range + Vector3.up * (fields.Height * 0.5f);

            flash.widthMultiplier = width;
            flash.SetPosition(0, from);
            flash.SetPosition(1, to);
            flash.enabled = true;

            flashTimer = Mathf.Max(0.01f, fields.FlashSeconds);
        }

        void TickFlash(float deltaTime)
        {
            if (flashTimer <= 0f) return;

            flashTimer -= deltaTime;
            if (flashTimer <= 0f) HideFlash();
        }

        void EnsureFlash()
        {
            if (flash != null) return;

            flashMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                color = new Color(0.4f, 0.85f, 1f, 0.9f)
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
            flashTimer = 0f;
            if (flash != null) flash.enabled = false;
        }

#if UNITY_EDITOR
        public override void DrawGizmos()
        {
            if (fields == null) return;

            UnityEditor.Handles.color = new Color(0.4f, 0.85f, 1f);
            UnityEditor.Handles.DrawWireDisc(Origin.position, Vector3.up, Range);

            // 만충 폭의 판정 박스를 정면 방향으로 그려 둔다 — 저작한 폭이 타일 대비 얼마나 되는지
            // 씬 뷰에서 바로 읽히지 않으면 밸런싱 감각이 안 잡힌다.
            RampProfile ramp = fields.ChargeRamp;
            int maxStacks = ramp != null && ramp.IsAuthored ? ramp.MaxStacks : 0;
            float width = WidthAt(maxStacks);
            float range = Range;
            if (width <= 0f || range <= 0f) return;

            Vector3 dir = Origin.forward;
            Matrix4x4 previous = UnityEditor.Handles.matrix;
            UnityEditor.Handles.matrix = Matrix4x4.TRS(
                Origin.position + dir * (range * 0.5f) + Vector3.up * (fields.Height * 0.5f),
                Quaternion.LookRotation(dir),
                Vector3.one);
            UnityEditor.Handles.DrawWireCube(Vector3.zero, new Vector3(width, fields.Height, range));
            UnityEditor.Handles.matrix = previous;
        }
#endif
    }
}
