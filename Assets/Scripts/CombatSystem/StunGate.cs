using System.Collections.Generic;

namespace NorthLand.Combat
{
    // 스턴 축(#164). 소스별로 카운트해 마지막 스턴이 풀릴 때까지 정지를 유지한다.
    // MoveSpeedComposer와 같은 이유로 클래스로 뽑았다 — 지상·공중 이동 구현이 같은 규칙을 공유해야 한다.
    //
    // 속도 축(MoveSpeedComposer)으로 정지를 표현하지 않는 이유: 50% 상대 하한이 걸려 있어
    // 배수를 0으로 내려도 멈추지 않는다. 그 클램프는 감속으로 몬스터를 영구 정지시켜 웨이브를
    // 소프트락하는 경로를 막기 위한 의도된 장치이므로, 스턴이 우회하려 들 대상이 아니다.
    //
    // 이동 소유권(Enemy.MovementOwnedByBehavior)을 쓰지 않는 이유: 보스 BT 노드가 이미 소유권을 잡고
    // 반납하는 주인이라, 스턴이 같은 플래그를 건드리면 반납 시점이 엉켜 보스가 이동·근접 평타를 잃는다
    // (WL-118과 같은 축). 스턴을 독립 게이트로 두면 소유권 중에 스턴이 걸려도 서로를 지우지 않는다.
    public sealed class StunGate
    {
        private readonly HashSet<int> sources = new HashSet<int>();

        public bool IsStunned => sources.Count > 0;

        // 같은 sourceId를 다시 걸어도 중복 누적되지 않는다(지속시간 갱신은 부여자 쪽 책임).
        public void Add(int sourceId) => sources.Add(sourceId);

        public void Remove(int sourceId) => sources.Remove(sourceId);

        // TODO(pooling): 오브젝트 풀링 도입 시 재사용체가 이전 생명주기의 스턴을 물려받지 않도록
        //                OnEnable에서 호출해야 한다.
        public void Clear() => sources.Clear();
    }
}
