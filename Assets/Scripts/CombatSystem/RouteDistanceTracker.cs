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
        /// ⚠ **"완주"와 "경로를 모른다"를 같은 값으로 내면 안 된다.** 둘 다 0으로 냈다가 잡은 버그다 —
        /// `FirstTargeting`의 점수가 `-잔여거리`라 **0은 실제 후보(전부 음수) 전부를 이기는 최고점**이다.
        /// 경로가 비어 있는 몬스터(두 mover 모두 빈 경로 분기가 있고, 공중은 `LogError`까지 달려 있을
        /// 만큼 실제로 발생한다) 한 마리가 살아 있는 동안 맵의 **모든 타워가 선두 대신 그 한 마리에 붙는다.**
        /// 폴백(`Tower.FindTarget`)은 후보 **전원**이 순위 밖일 때만 도는 all-or-nothing이라 걸러주지 못한다.
        ///
        ///   0    = 완주(종점 도달) — "가장 앞선 적"이 맞다
        ///   NaN  = 경로를 모른다   — 순위 밖. 정책이 `TryGetRouteDistance`에서 걸러낸다
        public float Remaining(Vector3 position, IReadOnlyList<Vector3> route, int currentIndex)
        {
            // 경로 자체가 없다 = 진행도를 말할 수 없다.
            if (route == null || route.Count == 0 || currentIndex < 0) return float.NaN;

            // 완주. **`suffix.Count` 검사보다 먼저** 와야 한다 — 완주는 그쪽 조건도 함께 만족하므로
            // 순서가 바뀌면 종점에 닿은 적이 "모름"으로 빠진다.
            if (currentIndex >= route.Count) return 0f;

            // 경로는 있는데 누적이 없다(SetRoute 미호출) — 순서를 만들 수 없으니 모른다고 답한다.
            if (currentIndex >= suffix.Count) return float.NaN;

            return Vector3.Distance(position, route[currentIndex]) + suffix[currentIndex];
        }
    }
}
