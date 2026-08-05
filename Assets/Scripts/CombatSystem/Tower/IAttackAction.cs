namespace NorthLand.Combat
{
    // 공격 능력을 가진 액션의 공통 계약. **전달 방식**(투사체=`AttackAction` / 히트스캔=`HitscanAttackAction`)이
    // 달라도 "이 타워는 공격하는가"를 묻는 소비처는 구현체를 알 필요가 없다(#252).
    //
    // 이 인터페이스 없이 구상 액션으로 능력을 판정하면, 전달 방식을 하나 늘릴 때마다 판정하는 모든 곳이
    // 조용히 그 타워를 제외한다 — 버프 오라 대상 선정, 보스 마력 봉인 대상 선정, Tower의 IAttacker 4개가
    // 전부 그 대상이었다. 컴파일 에러가 아니라 "버프가 안 걸리는 타워"로 나타나므로 발견이 늦다.
    //
    // **`TowerAction`을 상속하지 않는 순수 인터페이스다.** 액션 구현체는 이미 `TowerAction`을 상속하고 있어
    // 클래스를 하나 더 끼울 수 없고, 공통 기반 클래스를 두면 액션 하나를 읽을 때 부모까지 따라가야 한다
    // (BuffAura/DebuffAura가 같은 이유로 독립 sealed 클래스인 것과 같은 관례).
    // `Tower.Has<T>()`/`Get<T>()`의 제약이 `TowerAction`이 아니라 `class`인 이유가 이것이다.
    public interface IAttackAction
    {
        float Damage { get; }

        float Range { get; }

        float Interval { get; }

        bool TryAttack(IDamageable target);
    }
}
