using UnityEngine;

namespace NorthLand.Combat
{
    [CreateAssetMenu(fileName = "EnemyData", menuName = "Combat/EnemyData")]
    public class EnemyData : ScriptableObject
    {
        public float maxHp;
        public float attackDamage;
        public float attackRange;
        public float attackInterval;
    }
}
