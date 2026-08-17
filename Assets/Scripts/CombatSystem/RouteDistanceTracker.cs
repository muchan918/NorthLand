using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    /// 경로 잔여 거리 계산기. 지상(`MonsterMove`)·공중(`FlyingMonsterMove`) 이동 컴포넌트가 공유한다 —
    /// `MoveSpeedComposer`·`StunGate`와 같은 축이다(두 mover가 같은 규칙을 각자 구현하면 갈라진다).
    ///
    /// "앞선 적 / 뒤처진 적" 조준 정책(#387)의 판정 근거다. ⚠ **본진까지의 직선거리로 대신할 수 없다** —
    /// 경로가 꺾이는 맵에서는 직선상 가까운 적이 경로상으로는 한참 뒤일 수 있어 순서가 그대로 뒤집힌다.
    ///
    /// 경로 확정 시 뒤에서부터 누적한 길이를 한 번 만들어 두므로 조회는 O(1)이다. 이 값은 매 재조준마다
    /// **타워 수 × 후보 수**만큼 읽히므로 조회가 O(경로 길이)면 감당할 수 없다.
    public sealed class RouteDistanceTracker
    {
        // suffix[i] = route[i]에서 종점까지의 경로 길이(마지막 항은 항상 0).
        readonly List<float> suffix = new List<float>();

        /// 경로가 확정될 때 1회 호출한다. 호출자가 이후 경로를 바꾸면 다시 불러야 한다.
        public void SetRoute(IReadOnlyList<Vector3> route)
        {
            suffix.Clear();
            if (route == null || route.Count == 0) return;

            for (int i = 0; i < route.Count; i++) suffix.Add(0f);

            for (int i = route.Count - 2; i >= 0; i--)
            {
                suffix[i] = suffix[i + 1] + Vector3.Distance(route[i], route[i + 1]);
            }
        }

        /// 현재 위치에서 종점까지 남은 경로 길이 = (다음 웨이포인트까지 직선거리) + (그 지점부터의 누적).
        ///
        /// 완주했거나 경로가 없으면 0이다 — 종점에 닿기 직전이라는 뜻이므로 "가장 앞선 적"으로
        /// 취급되는 것이 맞다. (경로 자체를 갖지 못하는 대상은 여기까지 오지 않는다 — 그쪽은
        /// `ITargetProfile.RemainingRouteDistance`가 NaN을 낸다.)
        public float Remaining(Vector3 position, IReadOnlyList<Vector3> route, int currentIndex)
        {
            if (route == null || currentIndex < 0 || currentIndex >= route.Count) return 0f;

            // SetRoute를 거치지 않은 경로(방어) — 누적을 모르면 순서를 만들 수 없으니 0으로 둔다.
            if (currentIndex >= suffix.Count) return 0f;

            return Vector3.Distance(position, route[currentIndex]) + suffix[currentIndex];
        }
    }
}
