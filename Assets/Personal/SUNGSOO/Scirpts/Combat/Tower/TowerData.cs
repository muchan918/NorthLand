using UnityEngine;

namespace NorthLand.Combat
{
    [CreateAssetMenu(fileName = "TowerData", menuName = "Combat/TowerData")]
    public class TowerData : ScriptableObject
    {
        public float attackDamage;
        public float attackRange;
        public float attackInterval;

        [Header("Projectile")]
        public GameObject projectilePrefab;   // 발사할 투사체 프리팹(Projectile 컴포넌트 포함)
        public float projectileSpeed;         // 투사체 이동 속도
    }
}