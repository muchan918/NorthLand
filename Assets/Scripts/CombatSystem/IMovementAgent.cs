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
