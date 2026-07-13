using UnityEngine;

namespace NorthLand.Combat
{
    [CreateAssetMenu(fileName = "SoldierData", menuName = "Combat/SoldierData")]
    public class SoldierData : ScriptableObject
    {
        public float maxHp;
        public float attackDamage;
        public float attackRange;
        public float attackInterval;
    }
}
