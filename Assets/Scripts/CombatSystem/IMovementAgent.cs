namespace NorthLand.Combat
{
    // 전투 AI(Enemy 등)가 구동하는 이동 액추에이터 계약. NavMeshAgent.isStopped처럼
    // "멈춤/재개"만 지시받는다. 이동 구현체(MonsterMove 등)는 이 계약만 구현하면
    // 전투 로직을 몰라도 되고, 전투 측은 이동 구현에 결합하지 않는다.
    public interface IMovementAgent
    {
        // true면 전진을 멈춘다(대상 공격 중 등). AI가 매 프레임 지시할 수 있다.
        bool IsStopped { get; set; }
    }
}
