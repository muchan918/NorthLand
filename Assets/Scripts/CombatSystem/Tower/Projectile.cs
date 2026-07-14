using UnityEngine;

namespace NorthLand.Combat
{
    // 타워가 발사하는 투사체. 목표를 향해 날아가 명중하면 데미지를 입힌다.
    public class Projectile : MonoBehaviour
    {
        // 모델 축 보정용. 진행 방향(+Z) 정렬 후 추가로 적용할 회전.
        // 예: 화살 모델이 수직(+Y)으로 세워져 있으면 X에 90(또는 -90)을 넣어 눕힌다.
        [SerializeField] Vector3 rotationOffset;

        IDamageable target;
        float damage;
        float speed;
        IAttacker source;

        // 타워가 발사 직후 호출해 투사체를 초기화한다.
        public void Init(IDamageable target, float damage, float speed, IAttacker source)
        {
            this.target = target;
            this.damage = damage;
            this.speed = speed;
            this.source = source;
        }

        void Update()
        {
            // target은 인터페이스 타입이라 Unity의 == null 오버로드가 적용되지 않는다.
            // MonoBehaviour로 캐스팅한 뒤 null 검사를 해야 파괴된 오브젝트를 정확히 걸러낸다.
            var targetObj = target as MonoBehaviour;
            if (targetObj == null || target.IsDead)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 targetPos = targetObj.transform.position;

            // 진행 방향을 바라보도록 회전 (모델 축은 rotationOffset으로 보정)
            Vector3 dir = targetPos - transform.position;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(rotationOffset);

            // 목표를 향해 이동
            transform.position = Vector3.MoveTowards(
                transform.position, targetPos, speed * Time.deltaTime);

            // 충분히 가까워지면 명중 처리
            if (Vector3.Distance(transform.position, targetPos) < 0.1f)
            {
                target.TakeDamage(new DamageInfo(damage, source));
                Destroy(gameObject);
            }
        }
    }
}
