using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NorthLand.Combat
{
    public class Tower : MonoBehaviour, IAttacker
    {
        [SerializeField] TowerAsset data;

        // TODO(TBD): 대상 탐지 필터링을 LayerMask로 할지 Tag로 할지 미확정. 임시 LayerMask.
        [SerializeField] LayerMask enemyLayerMask;

        float cooldownTimer;
        readonly Collider[] hitBuffer = new Collider[16];

        public Faction Faction => Faction.Player;

        // TowerType에 맞는 공통 공격 스탯 해석. Magic(또는 data 미할당)은 Attack 없음 → null.
        TowerAsset.AttackFields Attack => data == null ? null : data.TowerType switch
        {
            TowerType.Single => data.Single.Attack,
            TowerType.Area => data.Area.Attack,
            TowerType.Chain => data.Chain.Attack,
            _ => null,
        };

        // Magic 타워/미할당(Attack==null)에서도 안전하도록 null 가드(공개 IAttacker 계약).
        public float AttackDamage => Attack != null ? Attack.AttackDamage : 0f;
        public float AttackRange => Attack != null ? Attack.AttackRange : 0f;
        public float AttackInterval => Attack != null ? Attack.AttackInterval : 0f;

        void Update()
        {
            // 공격 스탯이 없는 타입(Magic 등)은 이 컴포넌트가 처리하지 않음
            if (Attack == null) return;
            if (DayNightManager.Instance != null &&
                DayNightManager.Instance.CurrentPhase != DayNightManager.Phase.Night) return;

            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer > 0f) return;

            var target = FindTarget();
            if (target != null && TryAttack(target))
                cooldownTimer = AttackInterval;
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;

            var atk = Attack;
            if (atk == null || atk.ProjectilePrefab == null) return false;

            var obj = Instantiate(atk.ProjectilePrefab, transform.position, Quaternion.identity);
            if (!obj.TryGetComponent<Projectile>(out var projectile))
            {
                Destroy(obj);   // Projectile 컴포넌트 없으면 스폰물 제거 후 실패
                return false;
            }

            // 타입별 명중 동작(단일/스플래시/체인)을 구성해 투사체에 전달
            projectile.Init(target, atk.AttackDamage, atk.ProjectileSpeed, this, BuildImpact());
            return true;
        }

        ProjectileImpact BuildImpact()
        {
            switch (data.TowerType)
            {
                case TowerType.Area:
                    return ProjectileImpact.MakeArea(data.Area.SplashRadius, enemyLayerMask);
                case TowerType.Chain:
                    var c = data.Chain;
                    return ProjectileImpact.MakeChain(
                        c.ChainRadius, c.MaxChainTargets, c.ChainDamageFalloff, enemyLayerMask);
                default:
                    return ProjectileImpact.MakeSingle();
            }
        }

        // 사거리 내 가장 가까운 적을 타겟으로 선정 (매 프레임 경로라 NonAlloc 유지)
        IDamageable FindTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, AttackRange, hitBuffer, enemyLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                var damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.Faction != Faction && !damageable.IsDead)
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
            if (Attack == null) return;
            Handles.color = Color.red;
            Handles.DrawWireDisc(transform.position, Vector3.up, Attack.AttackRange);
        }
#endif
    }
}