using UnityEngine;

namespace NorthLand.Combat
{
    // 투사체를 쏘는 타워의 행동. Single/Area/Chain은 별개 행동이 아니라 **명중 방식(ProjectileImpact)만**
    // 다르다 — 대상 탐색·쿨다운·발사 경로가 완전히 같아서, 이미 분리돼 있는 ProjectileImpact를 전략으로 쓴다.
    //
    // 런타임 AddComponent로 붙으므로 직렬화 필드가 없다. 모든 값은 TowerBuildContext로 주입받는다.
    [AddComponentMenu("")]   // 런타임 조립 전용 — Add Component 메뉴에 노출하지 않는다
    [DisallowMultipleComponent]
    public sealed class AttackBehaviour : MonoBehaviour, ITowerBehaviour
    {
        Tower owner;
        TowerAsset.AttackFields fields;
        ProjectileFlight flight;
        ProjectileImpact impact;
        Transform firePoint;

        // 대상 탐색용 마스크를 impact와 별도로 보관한다 — ProjectileImpact.MakeSingle()은 EnemyMask를
        // 채우지 않으므로(스플래시·체인만 마스크가 필요) impact에서 되읽으면 단일 타워가 아무도 못 찾는다.
        LayerMask enemyLayerMask;

        float cooldownTimer;
        readonly Collider[] hitBuffer = new Collider[16];

        public TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        // 최종 스탯 = SO 기본값 + 원장(owner.Stats) 합성. 기본값만 여기가 알고, modifier는 원장이 소유한다.
        public float Damage =>
            fields == null ? 0f : owner.Stats.Evaluate(TowerStat.AttackDamage, fields.AttackDamage);

        public float Range =>
            fields == null ? 0f : owner.Stats.Evaluate(TowerStat.AttackRange, fields.AttackRange);

        // 공격속도는 배율 스탯이라 기본값 1f로 평가한다. 속도가 오를수록 간격이 짧아지므로 나눈다.
        public float Interval =>
            fields == null
                ? 0f
                : fields.AttackInterval / Mathf.Max(owner.Stats.Evaluate(TowerStat.AttackSpeed, 1f), 0.01f);

        // 선택 사거리 원은 실제 교전 사거리를 그린다(원장 합성값 = 타일 버프·버프 오라 반영).
        public float DisplayRange => Range;

        public void Initialize(in TowerBuildContext context)
        {
            owner = context.Owner;
            fields = TowerBehaviourFactory.ResolveAttackFields(context.Asset);
            firePoint = context.FirePoint;
            enemyLayerMask = context.EnemyLayerMask;
            flight = BuildFlight(fields);
            impact = BuildImpact(context.Asset, context.EnemyLayerMask);
            cooldownTimer = 0f;
        }

        public void Dispose()
        {
            // 외부에 남기는 상태가 없다(투사체는 발사 후 독립). 쿨다운만 초기화해 재활성화 시 즉시 사격 가능.
            cooldownTimer = 0f;
        }

        // 정보 패널에 이 행동이 기여할 줄: 공격력 / 사거리 / 공격속도.
        // 배치 전 툴팁(TowerTooltipView)과 같은 포매터를 쓴다 — 값의 출처만 다르다(원장 합성값 vs SO 원본).
        public string DescribeStats()
            => fields == null ? null : TowerStatsFormatter.BuildAttackLines(Damage, Range, Interval);

        public void Tick(float deltaTime)
        {
            if (fields == null) return;

            cooldownTimer -= deltaTime;
            if (cooldownTimer > 0f) return;

            IDamageable target = FindTarget();
            if (target != null && TryAttack(target)) cooldownTimer = Interval;
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;
            if (fields == null || fields.ProjectilePrefab == null) return false;

            Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
            GameObject obj = Instantiate(fields.ProjectilePrefab, spawnPos, Quaternion.identity);
            if (!obj.TryGetComponent(out Projectile projectile))
            {
                Destroy(obj);   // Projectile 컴포넌트 없으면 스폰물 제거 후 실패
                return false;
            }

            // 데미지 소스는 owner다 — IAttacker 계약을 가진 쪽이 타워이므로 DamageInfo가 타워를 가리킨다.
            projectile.Init(target, Damage, owner, flight, impact);
            owner.RaiseFired();
            return true;
        }

        // "어떻게 날아갈지". 전부 SO가 정한다 — 예전에는 Speed만 SO였고 Mode/ArcHeight는 탄환 프리팹에
        // 박혀 있어, 같은 궤적을 만드는 값이 두 파일로 갈려 있었다(#274).
        static ProjectileFlight BuildFlight(TowerAsset.AttackFields fields)
            => fields == null
                ? default
                : new ProjectileFlight
                {
                    Mode = fields.Flight,
                    Speed = fields.ProjectileSpeed,
                    ArcHeight = fields.ArcHeight,
                };

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

            TowerAsset.AttackFields attack = TowerBehaviourFactory.ResolveAttackFields(asset);
            result.StunDuration = attack != null ? attack.OnHitStunDuration : 0f;
            return result;
        }

        // 사거리 내 가장 가까운 적을 타겟으로 선정 (매 프레임 경로라 NonAlloc 유지)
        IDamageable FindTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, Range, hitBuffer, enemyLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider hit = hitBuffer[i];
                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.Faction != owner.Faction && !damageable.IsDead)
                {
                    float sqrDistance = (hit.transform.position - transform.position).sqrMagnitude;
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
        void OnDrawGizmosSelected()
        {
            if (fields == null) return;
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, Range);
        }
#endif
    }
}
