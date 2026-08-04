using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;

// 반경 질의 공용 헬퍼(#234). EnemyUnitsInRangeCondition과 EnemyResolveTargetAction이 공유한다.
//
// 노드에 두지 않는 이유: 같은 코드를 두 노드에 복제하면 앞뒤 판정 기준이 갈라진다.
// EnemyAgent에 두지 않는 이유: EnemyAgent는 얇은 파사드로 유지하고, "몇 개인지 세는" 판단은
// 노드의 일이다. EnemyAgent에서는 레이어 마스크와 진영만 읽는다.
//
// 구체 클래스(Enemy / Tower의 내부)에 닿지 않는다. 대상 식별은 IDamageable 계약으로만 하고,
// 타워는 Tower.Active 정적 리스트를 순회한다.
//
// 네임스페이스를 두지 않는다 — 노드와 같은 규약.
public static class EnemyNodeQuery
{
    // 물리 질의 재사용 버퍼. BT는 프레임 내에서 순차 실행되고 아래 메서드들은
    // 버퍼 내용을 반환 전에 소비하므로 노드 간 공유해도 겹치지 않는다.
    private static readonly Collider[] HitBuffer = new Collider[64];

    // 같은 대상이 콜라이더 여러 개를 갖는 프리팹에서 중복 집계를 막는다.
    private static readonly HashSet<IDamageable> Seen = new HashSet<IDamageable>();

    // 반경 안에서 조건에 맞는 대상 수를 센다.
    public static int CountInRange(
        EnemyAgent agent,
        EnemyUnitFilter filter,
        EnemyRelativeDirection direction,
        float radius)
    {
        if (agent == null || radius <= 0f)
        {
            return 0;
        }

        if (filter == EnemyUnitFilter.Tower)
        {
            return CountTowersInRange(agent, direction, radius);
        }

        return CountDamageablesInRange(agent, filter, direction, radius);
    }

    // 반경 안에서 조건에 맞는 가장 가까운 대상을 찾는다. 없으면 null.
    public static GameObject FindNearest(
        EnemyAgent agent,
        EnemyUnitFilter filter,
        EnemyRelativeDirection direction,
        float radius)
    {
        if (agent == null || radius <= 0f)
        {
            return null;
        }

        return filter == EnemyUnitFilter.Tower
            ? FindNearestTower(agent, direction, radius)
            : FindNearestDamageable(agent, filter, direction, radius);
    }

    // 공격 타워인지. `EnemyUnitFilter.Tower`가 가리키는 집합의 정의이며,
    // 오라·유틸 계열(haste / poison / 이동속도 감소)은 여기서 빠진다.
    //
    // 모든 타워가 하나의 `Tower` 타입이 된 뒤로, 대상 집합을 **능력**(공격 행동 보유)으로 직접 묻는다.
    // 예전에는 `Tower.Active` 멤버십(오라 타워가 별개 클래스라 등록되지 않는다는 사실)이나
    // `AttackInterval > 0`(공격 스탯이 없으면 0을 반환한다는 사실)에 의존했는데, 둘 다 판정의 근거가
    // 다른 시스템의 구현 세부에 있어서 그쪽이 바뀌면 조용히 뒤집혔다.
    //
    // 이 판정이 지키는 설계 의도: **"봉인 중에도 감속은 살아남아 P1 파훼 수단이 유지된다."**
    // 감속 타워가 봉인 대상에 들어가면 그 의도가 깨진다 — `Docs/Monster/Boss/TankGraphSpec.md` 참고.
    public static bool IsAttackTower(Tower tower)
    {
        return tower != null && tower.Has<AttackAction>();
    }

    // 진행 방향 기준 앞뒤 판정. y를 버리고 수평면에서만 본다 —
    // 경로가 평면이고 대상의 높이 차이(공중 유닛·타워 높이)가 앞뒤 판정을 뒤집으면 안 된다.
    public static bool MatchesDirection(
        EnemyAgent agent,
        Vector3 point,
        EnemyRelativeDirection direction)
    {
        if (direction == EnemyRelativeDirection.Any)
        {
            return true;
        }

        Vector3 offset = point - agent.transform.position;
        offset.y = 0f;

        Vector3 forward = agent.Forward;
        forward.y = 0f;

        float dot = Vector3.Dot(forward, offset);

        return direction == EnemyRelativeDirection.Forward ? dot >= 0f : dot < 0f;
    }

    private static int CountTowersInRange(
        EnemyAgent agent,
        EnemyRelativeDirection direction,
        float radius)
    {
        float sqrRadius = radius * radius;
        Vector3 origin = agent.transform.position;
        int count = 0;

        for (int i = 0; i < Tower.Active.Count; i++)
        {
            Tower tower = Tower.Active[i];

            // 트리거와 실제 봉인 대상의 집합을 같게 유지한다 — 약화되지 않는 타워를 세면
            // 봉인해도 아무것도 안 걸리는 타워 뭉치에 P3가 발동한다.
            if (!IsAttackTower(tower))
            {
                continue;
            }

            Vector3 position = tower.transform.position;

            if ((position - origin).sqrMagnitude > sqrRadius)
            {
                continue;
            }

            if (!MatchesDirection(agent, position, direction))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static GameObject FindNearestTower(
        EnemyAgent agent,
        EnemyRelativeDirection direction,
        float radius)
    {
        float sqrRadius = radius * radius;
        Vector3 origin = agent.transform.position;

        GameObject nearest = null;
        float nearestSqrDistance = float.MaxValue;

        for (int i = 0; i < Tower.Active.Count; i++)
        {
            Tower tower = Tower.Active[i];

            // Count 쪽과 같은 집합을 본다 — 같은 Filter 값이 두 메서드에서 다른 집합을 뜻하면 함정이 된다.
            if (!IsAttackTower(tower))
            {
                continue;
            }

            Vector3 position = tower.transform.position;
            float sqrDistance = (position - origin).sqrMagnitude;

            if (sqrDistance > sqrRadius || sqrDistance >= nearestSqrDistance)
            {
                continue;
            }

            if (!MatchesDirection(agent, position, direction))
            {
                continue;
            }

            nearestSqrDistance = sqrDistance;
            nearest = tower.gameObject;
        }

        return nearest;
    }

    private static int CountDamageablesInRange(
        EnemyAgent agent,
        EnemyUnitFilter filter,
        EnemyRelativeDirection direction,
        float radius)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            agent.transform.position, radius, HitBuffer, agent.UnitLayerMask);

        Seen.Clear();
        int count = 0;

        for (int i = 0; i < hitCount; i++)
        {
            if (!TryAccept(agent, filter, direction, HitBuffer[i], out IDamageable damageable))
            {
                continue;
            }

            if (Seen.Add(damageable))
            {
                count++;
            }
        }

        Seen.Clear();
        return count;
    }

    private static GameObject FindNearestDamageable(
        EnemyAgent agent,
        EnemyUnitFilter filter,
        EnemyRelativeDirection direction,
        float radius)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            agent.transform.position, radius, HitBuffer, agent.UnitLayerMask);

        Vector3 origin = agent.transform.position;
        GameObject nearest = null;
        float nearestSqrDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            if (!TryAccept(agent, filter, direction, HitBuffer[i], out IDamageable damageable))
            {
                continue;
            }

            // IDamageable을 구현한 루트를 반환한다 — 콜라이더 자식이 아니라 대상 본체여야
            // 이후 노드(거리 판정·피해 적용)가 같은 오브젝트를 본다.
            GameObject candidate = damageable is Component component
                ? component.gameObject
                : HitBuffer[i].gameObject;

            float sqrDistance = (candidate.transform.position - origin).sqrMagnitude;

            if (sqrDistance >= nearestSqrDistance)
            {
                continue;
            }

            nearestSqrDistance = sqrDistance;
            nearest = candidate;
        }

        return nearest;
    }

    private static bool TryAccept(
        EnemyAgent agent,
        EnemyUnitFilter filter,
        EnemyRelativeDirection direction,
        Collider hit,
        out IDamageable damageable)
    {
        damageable = null;

        if (hit == null)
        {
            return false;
        }

        damageable = hit.GetComponentInParent<IDamageable>();

        if (damageable == null || damageable.IsDead)
        {
            damageable = null;
            return false;
        }

        // 자기 자신은 아군 집계에서 제외한다 — 보스가 자기를 세면 방어 태세 임계값이 1 어긋난다.
        if (damageable is Component self && self.gameObject == agent.gameObject)
        {
            damageable = null;
            return false;
        }

        bool isAlly = damageable.Faction == agent.Faction;

        if (filter == EnemyUnitFilter.Ally ? !isAlly : isAlly)
        {
            damageable = null;
            return false;
        }

        if (!MatchesDirection(agent, hit.transform.position, direction))
        {
            damageable = null;
            return false;
        }

        return true;
    }
}
