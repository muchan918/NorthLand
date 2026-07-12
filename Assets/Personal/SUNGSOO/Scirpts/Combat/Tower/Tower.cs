using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NorthLand.Combat
{
    public class Tower : MonoBehaviour, IAttacker
    {
        [SerializeField] TowerData data;

        // TODO(TBD): 대상 탐지 필터링을 LayerMask로 할지 Tag로 할지 미확정.
        //            현재는 임시로 LayerMask 방식 사용. 팀 컨벤션 회의 후 결정 및 수정 예정.
        [SerializeField] LayerMask enemyLayerMask;

        float cooldownTimer;

        // 타겟 탐색용 재사용 버퍼. 매 프레임 힙 할당을 피하기 위해 사용(최대 16개 감지).
        readonly Collider[] hitBuffer = new Collider[16];

        public Faction Faction => Faction.Player;
        public float AttackDamage => data.attackDamage;
        public float AttackRange => data.attackRange;
        public float AttackInterval => data.attackInterval;

        void Update()
        {
            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer > 0f) return;

            var target = FindTarget();
            if (target != null && TryAttack(target))
                cooldownTimer = AttackInterval;
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;
            if (data.projectilePrefab == null) return false;

            // 즉시 데미지 대신 투사체를 발사한다. 실제 데미지는 투사체가 명중할 때 적용됨.
            var obj = Instantiate(data.projectilePrefab, transform.position, Quaternion.identity);
            if (!obj.TryGetComponent<Projectile>(out var projectile))
            {
                Destroy(obj);   // Projectile 컴포넌트가 없으면 스폰물을 제거하고 공격 실패 처리
                return false;
            }

            projectile.Init(target, AttackDamage, data.projectileSpeed, this);
            return true;
        }

        // 사거리 내에서 가장 가까운 몬스터를 타겟으로 선정
        IDamageable FindTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, AttackRange, hitBuffer, enemyLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                // 콜라이더가 자식(모델)에 있고 Enemy 스크립트가 부모에 있어도 찾도록 부모까지 탐색
                var damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable != null
                    && damageable.Faction != Faction
                    && !damageable.IsDead)
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
        // Scene 뷰에서 타워를 선택하면 사거리를 바닥 평면(x,z) 원으로 표시
        void OnDrawGizmosSelected()
        {
            if (data == null) return;
            Handles.color = Color.red;
            Handles.DrawWireDisc(transform.position, Vector3.up, data.attackRange);
        }
#endif
    }
}
