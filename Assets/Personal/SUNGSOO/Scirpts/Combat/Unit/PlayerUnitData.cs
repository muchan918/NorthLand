using UnityEngine;

namespace NorthLand.Combat
{
    [CreateAssetMenu(fileName = "PlayerUnitData", menuName = "Combat/PlayerUnitData")]
    public class PlayerUnitData : ScriptableObject
    {
        public float maxHp;
        public float attackDamage;
        public float attackRange;
        public float attackInterval;
    }
}
