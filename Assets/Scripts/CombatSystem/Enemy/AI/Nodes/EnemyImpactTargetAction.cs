using NorthLand.Combat;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 대상에 실효 이동속도 비례 피해를 준다(#234). P1 돌진의 충돌 피해.
//
// 피해 입력이 패턴 배수가 아니라 EnemyAgent.EffectiveMoveSpeed인 것이 이 노드의 핵심이다.
// 배수를 읽으면 감속 디버프가 반영되지 않아 "이동속도 감소 타워로 돌진을 파훼한다"가
// 기능적으로 성립하지 않는다.
//
// 실효 속도가 MinSpeed 미만이면 피해 없이 성공한다 — 감속으로 충분히 늦춘 플레이어가
// 충돌을 완전히 무력화하는 보상 구간이다. 실패로 두면 상위 시퀀스가 매 틱 재시도한다.
//
// 상태를 바꾸지 않으므로 원복할 것이 없다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Enemy Impact Target",
    description: "대상에 실효 이동속도 비례 피해를 준다. 실효 속도가 하한 미만이면 피해 없이 성공.",
    story: "[Agent] impacts [Target] for [DamagePerSpeedUnit] per speed unit",
    category: "Action/Enemy",
    id: "016e665298c54a71b78d5524a2a29831")]
public partial class EnemyImpactTargetAction : Action
{
    [SerializeReference] public BlackboardVariable<EnemyAgent> Agent;

    [SerializeReference] public BlackboardVariable<GameObject> Target;

    // 실효 이동속도 1당 피해량. 최종 피해 = 실효 속도 × 이 값.
    [SerializeReference] public BlackboardVariable<float> DamagePerSpeedUnit;

    // 이 속도 미만이면 피해를 주지 않는다.
    [SerializeReference] public BlackboardVariable<float> MinSpeed;

    protected override Status OnStart()
    {
        EnemyAgent agent = Agent?.Value;

        if (agent == null)
        {
            LogFailure("Enemy Impact Target: Agent가 지정되지 않았습니다.");
            return Status.Failure;
        }

        GameObject target = Target?.Value;

        if (target == null)
        {
            LogFailure("Enemy Impact Target: Target이 지정되지 않았습니다.");
            return Status.Failure;
        }

        float speed = agent.EffectiveMoveSpeed;
        float minSpeed = MinSpeed != null ? MinSpeed.Value : 0f;

        if (speed < minSpeed)
        {
            return Status.Success;
        }

        // 대상 본체가 콜라이더 자식일 수 있으므로 부모까지 올라가 계약을 찾는다.
        IDamageable damageable = target.GetComponentInParent<IDamageable>();

        if (damageable == null)
        {
            LogFailure($"Enemy Impact Target: '{target.name}'에 IDamageable이 없어 피해를 줄 수 없습니다.");
            return Status.Failure;
        }

        if (damageable.IsDead)
        {
            return Status.Success;
        }

        float damagePerUnit = DamagePerSpeedUnit != null ? DamagePerSpeedUnit.Value : 0f;

        // Source는 IAttacker 계약이며 EnemyAgent는 이를 구현하지 않는다.
        // 충돌 피해는 반격·처치 기여 집계 대상이 아니므로 null로 둔다(Enemy의 근접 공격과 구분).
        //
        // 충돌음의 주인은 때리는 쪽이다(`EnemyAsset.ImpactSfx`, §6.4) — 평타 공격음과 같은 규칙이고,
        // 소리를 가르기 위해 받는 쪽에 특례를 얹지 않는다. 스로틀은 걸지 않는다: 돌진은 `tank` P1의
        // 1회 한정 패턴이라 자기끼리 겹칠 일이 없고, 걸어 두면 오히려 놓칠 위험만 생긴다.
        //
        // 우선순위 `Normal`: 평타(`Low`)보다 무거운 단발 대타격이지만 스킬음(`High`)보다는 아래다.
        // ⚠ 본진 경고음과는 **비교 대상이 아니다** — 그쪽은 이 풀을 아예 쓰지 않는 2D 경로라
        // (`Sfx.BaseDamaged` → `AudioManager.PlaySfx`) 보이스 상한을 두고 경쟁하지 않는다.
        PlayImpactSfx(agent, speed * damagePerUnit);

        damageable.TakeDamage(new DamageInfo(speed * damagePerUnit, null));

        return Status.Success;
    }

    /// 돌진 충돌음. 클립은 가해자의 `EnemyAsset`이 든다.
    ///
    /// `EnemyAgent`가 `Enemy`를 공개하지 않으므로 같은 오브젝트에서 집어온다 — BT 그래프에
    /// 블랙보드 변수를 추가하지 않기 위한 선택이다(그래프 저작을 건드리면 이 노드를 쓰는
    /// 트리마다 배선이 늘고, 잊은 트리는 조용히 무음이 된다).
    ///
    /// 위치는 **가해자**다. 돌진은 자기 몸으로 들이받는 것이라 소리가 나는 곳이 곧 보스의 위치다.
    private static void PlayImpactSfx(EnemyAgent agent, float damage)
    {
        if (damage <= 0f)
        {
            return;
        }

        var enemy = agent.GetComponent<Enemy>();
        var asset = enemy != null ? enemy.Asset : null;

        if (asset == null)
        {
            return;
        }

        CombatSfx.Play(
            asset.ImpactSfx,
            enemy.HitPosition != null ? enemy.HitPosition.position : agent.transform.position,
            volumeScale: asset.ImpactSfxVolume,
            priority: CombatSfxPriority.Normal);
    }
}
