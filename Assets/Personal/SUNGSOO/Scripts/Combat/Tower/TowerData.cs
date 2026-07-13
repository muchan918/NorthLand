using UnityEngine;

namespace NorthLand.Combat
{
    [CreateAssetMenu(fileName = "TowerData", menuName = "Combat/TowerData")]
    public class TowerData : ScriptableObject
    {
        public float attackDamage;
        public float attackRange;
        public float attackInterval;
    }
}