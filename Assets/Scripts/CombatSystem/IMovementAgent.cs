using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    public interface IMovementAgent
    {
        bool IsStopped { get; set; }

        void SetMoveSpeed(float moveSpeed);

        // ── 이동속도 다축 합성 계약(#233) ─────────────────────────────
        // 최종 이동속도 = 기준 속도(SetMoveSpeed) × 패턴 배수 × Π 디버프 배수  (하한 클램프)
        //
        // 두 축을 분리하는 이유: 보스 BT의 돌진 가속(패턴 축)과 이동속도 감소 타워(디버프 축)가
        // 같은 값을 놓고 경쟁해야 "가속하는 보스를 감속 타워가 끌어내린다"가 별도 밸런싱 없이 성립한다.
        // 단일 스칼라 덮어쓰기였을 때는 어느 한쪽이 다른 쪽을 지웠다.
        //
        // 완전 정지는 이 축이 아니라 IsStopped로 표현한다 — 하한 클램프가 걸려 있어
        // 배수를 0으로 내려도 멈추지 않는다. 감속으로 몬스터를 영구 정지시켜
        // 웨이브를 소프트락하는 경로를 막기 위함이다.

        // 디버프까지 반영된 최종 이동속도. 보스 돌진 충돌 피해처럼
        // "배수가 아니라 실제로 얼마나 빠른가"가 입력인 계산이 이 값을 읽는다.
        float EffectiveMoveSpeed { get; }

        // 패턴 축. BT 노드가 소유한다(돌진 가속 / 방어 태세 크롤). 음수는 0으로 클램프된다.
        float PatternSpeedFactor { get; set; }

        // 디버프 축. 소스별로 곱산 중첩된다(같은 sourceId는 갱신만).
        // 배율 1 미만이면 감속, 초과면 가속. 해제는 RemoveSpeedDebuff로만 한다.
        void AddSpeedDebuff(int sourceId, float factor);

        void RemoveSpeedDebuff(int sourceId);

        // ── 스턴 축(#164) ─────────────────────────────
        // 속도 축·이동 소유권 어느 쪽과도 독립된 정지 축이다. 속도 축은 하한 클램프 때문에 배수를 0으로
        // 내려도 멈추지 않고, 이동 소유권은 보스 BT가 잡고 반납하는 주인이라 스턴이 끼어들면 반납 시점이
        // 엉킨다. 그래서 정지 의미를 갖는 CC는 이 축으로만 표현한다.
        //
        // 소프트락 우려가 속도 축과 다른 이유: 스턴은 지속시간이 있는 효과라 부여자가 만료 시 반드시
        // 해제한다(StatusEffectHandler가 소진 소유). 반면 감속은 갱신이 계속 들어오면 무한히 유지된다.
        //
        // 소스별로 카운트되므로 여러 스턴이 겹쳐도 마지막 하나가 풀릴 때까지 정지가 유지된다.
        bool IsStunned { get; }

        void AddStun(int sourceId);

        void RemoveStun(int sourceId);

    }
    public interface IRouteMovementAgent : IMovementAgent
    {
        MovementMode SupportedMode { get; }

        bool HasRouteRemaining { get; }

        event Action RouteCompleted;

        void SetRoute(IReadOnlyList<Vector3> routePoints);

        void SetMoveEnabled(bool enabled);
    }

}
