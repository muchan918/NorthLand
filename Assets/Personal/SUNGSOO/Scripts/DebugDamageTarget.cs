using UnityEngine;
using NorthLand.Combat;

namespace NorthLand.Sungsoo
{
    // 디버프 타워 검증용 더미 타깃. 전체 Enemy/EnemyAsset 파이프라인 없이 IDamageable만 구현한다.
    // 콜라이더 + enemyLayerMask 레이어에 두면 DebuffTower가 인식한다. 검증 전용.
    public class DebugDamageTarget : MonoBehaviour, IDamageable
    {
        [SerializeField] float maxHp = 100f;
        public float currentHp;   // 인스펙터/로그로 HP 관찰용 (public)

        public Faction Faction => Faction.Enemy;
        public bool IsDead => currentHp <= 0f;

        public Transform HitPosition => throw new System.NotImplementedException();

        void Awake() => currentHp = maxHp;

        public void TakeDamage(DamageInfo info)
        {
            currentHp -= info.Amount;
        }
    }
}
