using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 투사체를 쏘는 액션. Single/Area/Chain은 별개 액션이 아니라 **명중 방식(ProjectileImpact)만**
    // 다르다 — 대상 탐색·쿨다운·발사 경로가 완전히 같아서, 이미 분리돼 있는 ProjectileImpact를 전략으로 쓴다.
    // 마찬가지로 유도/포격은 ProjectileFlight가 갈라낸다. 둘 다 SO가 정한다(#274).
    [Serializable]
    public sealed class AttackAction : TowerAction
    {
        // ── 런타임 상태 (직렬화 금지 — TowerAction 규칙 ③) ──────────────────
        [NonSerialized] TowerAsset.AttackFields fields;
        [NonSerialized] ProjectileFlight flight;
        [NonSerialized] ProjectileImpact impact;
        [NonSerialized] List<HitEffect> effects;   // SO의 Effects — 투사체에 실어 보낸다

        // 대상 탐색용 마스크를 impact와 별도로 보관한다 — ProjectileImpact.MakeSingle()은 EnemyMask를
        // 채우지 않으므로(스플래시·체인만 마스크가 필요) impact에서 되읽으면 단일 타워가 아무도 못 찾는다.
        [NonSerialized] LayerMask enemyLayerMask;

        [NonSerialized] float cooldownTimer;
        [NonSerialized] Collider[] hitBuffer;

        // 이 타워가 애초에 쏠 수 있는가(저작이 갖춰졌는가). 조립 시 1회 판정한다 —
        // 판정 재료(수치·탄환 프리팹·비행 부품)가 전부 SO 값이라 Initialize 사이에 바뀌지 않는다.
        [NonSerialized] bool canFire;

        public override TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        // 최종 스탯 = SO 기본값 + 원장(Owner.Stats) 합성. 기본값만 여기가 알고, modifier는 원장이 소유한다.
        public float Damage =>
            fields == null ? 0f : Owner.Stats.Evaluate(TowerStat.AttackDamage, fields.AttackDamage);

        public float Range =>
            fields == null ? 0f : Owner.Stats.Evaluate(TowerStat.AttackRange, fields.AttackRange);

        // 공격속도는 배율 스탯이라 기본값 1f로 평가한다. 속도가 오를수록 간격이 짧아지므로 나눈다.
        public float Interval =>
            fields == null
                ? 0f
                : fields.AttackInterval / Mathf.Max(Owner.Stats.Evaluate(TowerStat.AttackSpeed, 1f), 0.01f);

        // 선택 사거리 원은 실제 교전 사거리를 그린다(원장 합성값 = 타일 버프·버프 오라 반영).
        public override float DisplayRange => Range;

        protected override void OnInitialize(TowerAsset asset)
        {
            fields = asset.Attack;
            effects = asset.Effects;
            enemyLayerMask = Owner.EnemyLayerMask;
            flight = fields?.Flight;   // SO의 부품을 그대로 쓴다 — 무상태라 공유해도 안전하다
            impact = BuildImpact(asset, enemyLayerMask);
            cooldownTimer = 0f;

            // TryAttack이 실패하는 조건과 **같은 것**을 미리 판정해 둔다(아래 Tick 주석 참조).
            canFire = fields != null && fields.ProjectilePrefab != null && flight != null;

            // 매 프레임 경로라 NonAlloc 버퍼를 쓴다. 직렬화되지 않으므로 여기서 만든다.
            hitBuffer ??= new Collider[16];
        }

        public override void Dispose()
        {
            // 외부에 남기는 상태가 없다(투사체는 발사 후 독립). 쿨다운만 초기화해 재활성화 시 즉시 사격 가능.
            cooldownTimer = 0f;
        }

        // 정보 패널에 이 액션이 기여할 줄: 공격력 / 사거리 / 공격속도.
        // 배치 전 툴팁(TowerTooltipView)과 같은 포매터를 쓴다 — 값의 출처만 다르다(원장 합성값 vs SO 원본).
        public override string DescribeStats()
        {
            if (fields == null) return null;

            string text = TowerStatsFormatter.BuildAttackLines(Damage, Range, Interval);
            return TowerStatsFormatter.Join(text, DescribeEffects(effects, Owner));
        }

        /// 효과 목록을 설명 줄로 잇는다. 공격 액션과 디버프 오라가 같은 표기를 공유한다.
        ///
        /// ⚠ **적용부와 같은 술어로 걸러야 한다.** 합성 계승(#274 Phase 5)으로 꺼진 효과를 그대로 표기하면
        /// "정보 패널엔 독이 있다는데 실제로는 안 걸리는" 어긋남이 생긴다 — 표시부와 적용부가 규칙을 각자
        /// 쓰는 것이 WL-079/WL-130이 지적한 문제였다. 그래서 `stats`가 아니라 **`owner`를 통째로** 받는다:
        /// 원장과 필터가 같은 곳에서 나오므로 호출부가 필터를 빠뜨릴 수 없다.
        internal static string DescribeEffects(List<HitEffect> effects, Tower owner)
        {
            if (effects == null || effects.Count == 0 || owner == null) return null;

            string result = null;
            for (int i = 0; i < effects.Count; i++)
            {
                HitEffect effect = effects[i];
                if (effect == null) continue;
                if (!owner.IsEffectActive(effect.Kind)) continue;   // 계승으로 꺼진 효과는 표기하지 않는다

                string line = effect.Describe(owner.Stats);
                if (string.IsNullOrEmpty(line)) continue;
                result = result == null ? line : $"{result}\n{line}";
            }
            return result;
        }

        public override void Tick(float deltaTime)
        {
            // ⚠ **저작이 비어 있으면 Tick 자체에 들어가지 않는다.**
            //
            // 쿨다운은 `TryAttack`이 성공했을 때만 리셋된다. 그래서 쏠 수 없는 타워는 매 프레임
            // `cooldownTimer <= 0`인 채로 FindTarget()을 부르고, 그 안의 OverlapSphereNonAlloc이
            // 초당 60번 돈다 — 아무것도 못 하면서 물리 예산만 태우는 것이다.
            // (`lightning_tower`류의 전 필드 0 SO가 배치되면 정확히 이 상태가 된다, WL-001.)
            //
            // 타워 수가 계속 늘어나는 장르라 "무동작이면 비용도 0"이어야 한다.
            if (!canFire) return;

            cooldownTimer -= deltaTime;
            if (cooldownTimer > 0f) return;

            IDamageable target = FindTarget();
            if (target != null && TryAttack(target)) cooldownTimer = Interval;
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;
            if (fields == null || fields.ProjectilePrefab == null) return false;
            if (flight == null) return false;   // 비행 부품 미저작 — TowerAsset.OnValidate가 저장 시점에 경고한다

            // firePoint 미할당이면 타워 루트(바닥)에서 생성 — 하위 호환(ArcherTower.prefab이 그렇다).
            Transform firePoint = Owner.FirePoint;
            Vector3 spawnPos = firePoint != null ? firePoint.position : Origin.position;

            GameObject obj = UnityEngine.Object.Instantiate(
                fields.ProjectilePrefab, spawnPos, Quaternion.identity);

            if (!obj.TryGetComponent(out Projectile projectile))
            {
                UnityEngine.Object.Destroy(obj);   // Projectile 컴포넌트 없으면 스폰물 제거 후 실패
                return false;
            }

            // 데미지 소스는 Owner다 — IAttacker 계약을 가진 쪽이 타워이므로 DamageInfo가 타워를 가리킨다.
            projectile.Init(target, Damage, Owner, flight, impact, effects);
            Owner.RaiseFired();
            return true;
        }

        // "어떻게 날아갈지". 전부 SO가 정한다 — 예전에는 Speed만 SO였고 Mode/ArcHeight는 탄환 프리팹에
        // 박혀 있어, 같은 궤적을 만드는 값이 두 파일로 갈려 있었다(#274).
        // "터지면 누구를 때릴지". 발사마다 재구성할 이유가 없어 조립 시 1회 만든다
        // (ProjectileImpact는 struct라 발사 시 복사되어 전달된다).
        static ProjectileImpact BuildImpact(TowerAsset asset, LayerMask enemyLayerMask)
        {
            ProjectileImpact result = asset.Impact switch
            {
                ImpactKind.Area => ProjectileImpact.MakeArea(asset.SplashRadius, enemyLayerMask),
                ImpactKind.Chain => ProjectileImpact.MakeChain(
                    asset.ChainRadius, asset.MaxChainTargets, asset.ChainDamageFalloff, enemyLayerMask),
                _ => ProjectileImpact.MakeSingle(),
            };

            // 명중 효과(스턴 등)는 여기 섞지 않는다 — Projectile이 세 경로 공통 지점에서 Effects를 적용한다.
            return result;
        }

        // 사거리 내 가장 가까운 적을 타겟으로 선정 (매 프레임 경로라 NonAlloc 유지)
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
                if (damageable != null && damageable.Faction != Owner.Faction && !damageable.IsDead)
                {
                    float sqrDistance = (hit.transform.position - origin).sqrMagnitude;
                    if (sqrDistance < closestSqrDistance)
                    {
                        closestSqrDistance = sqrDistance;
                        closest = damageable;
                    }
                }
            }
            return closest;
        }

#if UNITY_EDITOR
        public override void DrawGizmos()
        {
            if (fields == null) return;
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(Origin.position, Vector3.up, Range);
        }
#endif
    }
}
