using UnityEngine;
using NorthLand.Combat;

// SkillManagerTest 전용 더미 피격 대상. Enemy.cs 전체(EnemyAsset 의존)를 쓰지 않고
// 최소 IDamageable만 구현해 SkillManager.CastAt의 데미지 적용 자체만 독립적으로 검증한다.
[RequireComponent(typeof(SphereCollider))]
public class DummyDamageable : MonoBehaviour, IDamageable
{
    public float DamageTaken { get; private set; }

    public Faction Faction => Faction.Enemy;
    public bool IsDead => false;

    private void Awake()
    {
        gameObject.layer = LayerMask.NameToLayer("Enemy");
    }

    public void TakeDamage(DamageInfo info) => DamageTaken += info.Amount;
}
