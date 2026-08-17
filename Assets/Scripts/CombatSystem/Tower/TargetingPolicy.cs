using System;
using UnityEngine;

namespace NorthLand.Combat
{
    /// 스캔이 이미 추려낸 후보 1기. 정책은 이 값만 보고 점수를 낸다 —
    /// 후보 판별(생사·팩션·사거리)과 물리 조회는 호출부(`Tower.FindTarget`)가 끝냈다.
    public readonly struct TargetCandidate
    {
        public readonly IDamageable Target;

        /// 체력·경로 진행도. **null일 수 있다** — 경로를 따라 오는 적만 구현한다(`ITargetProfile` 주석).
        public readonly ITargetProfile Profile;

        /// 타워 원점으로부터의 제곱 거리. 스캔이 이미 계산했으므로 정책이 다시 재지 않는다.
        public readonly float SqrDistance;

        public TargetCandidate(IDamageable target, ITargetProfile profile, float sqrDistance)
        {
            Target = target;
            Profile = profile;
            SqrDistance = sqrDistance;
        }
    }

    /// "사거리 안의 여러 적 중 **누구를** 겨누는가"의 규칙(#387). 타워 SO가 하나를 고른다.
    ///
    /// ★ 나열된 정책이 전부 **"점수 하나를 최대화한다"**로 환원된다는 것이 이 설계의 전부다:
    ///   가까운 적 = -거리 최대 / 앞선 적 = -잔여경로 최대 / 체력 높은 적 = +체력 최대.
    ///   그래서 정책은 점수 함수 하나면 되고, **스캔 루프는 `Tower.FindTarget` 한 곳에 그대로 남는다** —
    ///   #336이 "타워가 겨누는 대상"의 단일 출처로 만든 지점이 정책을 늘려도 갈라지지 않는다.
    ///   (여기가 정책마다 자기 탐색을 갖는 구조였다면 조준 연출과 실제 사격이 곧바로 어긋난다.)
    ///
    /// 새 조준 방식 = 이 클래스 파생 1개. enum·switch·에디터 어디도 고치지 않는다 —
    /// `ProjectileFlight`(비행)·`HitEffect`(명중효과)·`TowerAction`(타워 행동)이 쓰는 것과 같은 축이다.
    [Serializable]
    public abstract class TargetingPolicy
    {
        /// 점수가 **가장 높은** 후보 1기가 선택된다(동점이면 먼저 스캔된 쪽).
        ///
        /// ⚠ `float.NegativeInfinity` = "이 정책으로는 순위를 매길 수 없다". 그런 후보만 남으면
        /// 호출부가 **최근접으로 폴백**한다. 여기서 대신 거리 점수를 돌려주면 안 된다 —
        /// 체력 40점과 거리 -25점처럼 **축이 다른 값이 한 비교에 섞여** 순위가 무의미해진다.
        public abstract float Score(in TargetCandidate candidate);

        /// 정보 패널 표기용 이름.
        public abstract string DisplayName { get; }

        /// 인게임에서 순환 선택할 수 있는 정책 목록 — **표시 순서가 곧 이 배열 순서**다.
        ///
        /// ⚠ **새 정책을 추가하면 여기에도 넣어야 한다.** 파생 클래스만 만들면 SO 드롭다운에는 뜨지만
        /// (에디터는 `TypeCache`로 훑는다) 인게임 순환에는 나타나지 않는다. 런타임에는 `TypeCache`가
        /// 없고, 리플렉션 열거는 IL2CPP 스트리핑에 조용히 걸릴 수 있어 목록을 명시적으로 둔다.
        ///
        /// 정책은 수치 필드가 없는 무상태 객체라 인스턴스를 공유해도 안전하다
        /// (`ProjectileFlight`를 SO에서 그대로 공유하는 것과 같은 이유).
        public static readonly TargetingPolicy[] All =
        {
            new FirstTargeting(),
            new LastTargeting(),
            new NearestTargeting(),
            new HighestHpTargeting(),
            new LowestHpTargeting(),
        };

        /// 저작되지 않은 SO가 쓰는 기본 정책 = **앞선 적**.
        ///
        /// ⚠ **이것은 의도된 거동 변경이다**(회귀가 아니다). 리팩토링 이전의 하드코딩 거동은 최근접이었고
        /// 도입 초기에도 그것을 기본값으로 뒀지만, 타워 디펜스에서 타워가 존재하는 이유는 **누수 방지**라
        /// "본진에 가장 가까운 적부터 친다"가 장르 기본값이다(BTD류의 First가 같은 선택). 최근접은
        /// "타워 옆에 있으니까 친다"일 뿐 플레이어의 목적과 무관하다.
        ///
        /// 그래서 저작이 비어 있는 SO 전체(현재 21종)의 조준이 함께 바뀐다 — 개별 타워를 예전대로
        /// 두려면 그 SO에서 `NearestTargeting`을 **명시적으로** 고를 것.
        ///
        /// (`All`보다 **뒤에** 선언해야 한다 — 정적 필드 초기화는 선언 순서대로 돈다.)
        public static readonly TargetingPolicy Default = All[0];

        /// 목록에서 `step`칸 옮긴 정책. 인게임 좌/우 전환 버튼의 단일 계산 지점이다.
        ///
        /// ⚠ **참조가 아니라 타입으로 현재 위치를 찾는다.** SO가 물고 있는 인스턴스는 에디터가 따로
        /// 만든 것이라 `All`의 항목과 참조가 다르다 — 참조로 찾으면 항상 "목록 밖"으로 판정돼
        /// 저작값이 `FirstTargeting`인 타워에서 ▶를 눌러도 같은 정책이 다시 나오는 것처럼 보인다.
        public static TargetingPolicy Cycle(TargetingPolicy current, int step)
        {
            int index = 0;
            for (int i = 0; i < All.Length; i++)
            {
                if (current != null && All[i].GetType() == current.GetType())
                {
                    index = i;
                    break;
                }
            }

            // 음수 step에서도 감기도록 두 번 보정한다(C# %는 음수를 그대로 낸다).
            int next = ((index + step) % All.Length + All.Length) % All.Length;
            return All[next];
        }

        /// 경로 진행도를 아는 후보인가. NaN(경로 없음)은 순위 밖이다 — `ITargetProfile` 주석 참조.
        protected static bool TryGetRouteDistance(in TargetCandidate candidate, out float distance)
        {
            distance = candidate.Profile?.RemainingRouteDistance ?? float.NaN;
            return !float.IsNaN(distance);
        }
    }

    /// 가장 가까운 적. 리팩토링 이전의 하드코딩 거동이 이것이었지만 **기본값은 아니다**(`Default` 주석 참조) —
    /// 예전 거동을 유지하려는 타워는 SO에서 이것을 명시적으로 골라야 한다.
    [Serializable]
    public sealed class NearestTargeting : TargetingPolicy
    {
        public override float Score(in TargetCandidate candidate) => -candidate.SqrDistance;

        public override string DisplayName => "가까운 적";
    }

    // "타워로부터 가장 먼 적"(FarthestTargeting)은 두지 않는다. `NearestTargeting`의 부호만 뒤집으면
    // 공짜로 나오지만 쓸모가 없다 — 거리만으로는 **다가오는 적과 멀어지는 적을 구분할 수 없어서**
    // "사거리를 벗어나기 직전을 잡는다"가 성립하지 않고, 그 의도는 진행 방향을 실제로 아는
    // `First`/`Last`가 이미 제대로 한다. 대칭이 된다고 선택지가 되는 것은 아니다.

    /// 본진에 가장 가까운(=경로를 가장 많이 지나온) 적. **저작이 비었을 때의 기본값**이다 —
    /// 타워가 존재하는 이유가 누수 방지라 장르 기본값이 여기다.
    [Serializable]
    public sealed class FirstTargeting : TargetingPolicy
    {
        public override float Score(in TargetCandidate candidate)
            => TryGetRouteDistance(candidate, out float remaining) ? -remaining : float.NegativeInfinity;

        public override string DisplayName => "앞선 적";
    }

    /// 경로를 가장 적게 지나온 적. 뒤쪽부터 깎아 앞줄이 두꺼워지는 것을 막는 용도.
    [Serializable]
    public sealed class LastTargeting : TargetingPolicy
    {
        public override float Score(in TargetCandidate candidate)
            => TryGetRouteDistance(candidate, out float remaining) ? remaining : float.NegativeInfinity;

        public override string DisplayName => "뒤처진 적";
    }

    /// 현재 체력이 가장 높은 적. 탱커·보스를 물고 늘어지는 고화력 타워용.
    ///
    /// 최대 체력이 아니라 **현재 체력** 기준이다 — 이미 깎인 탱커보다 멀쩡한 일반 적을 우선할지가
    /// 갈리는 지점인데, 플레이어가 화면에서 보는 것이 현재 체력이라 그쪽에 맞춘다.
    [Serializable]
    public sealed class HighestHpTargeting : TargetingPolicy
    {
        public override float Score(in TargetCandidate candidate)
            => candidate.Profile != null ? candidate.Profile.CurrentHp : float.NegativeInfinity;

        public override string DisplayName => "체력 높은 적";
    }

    /// 현재 체력이 가장 낮은 적. 마무리를 몰아 처치 수를 늘리는 용도(킬스택 성장 타워와 궁합).
    [Serializable]
    public sealed class LowestHpTargeting : TargetingPolicy
    {
        public override float Score(in TargetCandidate candidate)
            => candidate.Profile != null ? -candidate.Profile.CurrentHp : float.NegativeInfinity;

        public override string DisplayName => "체력 낮은 적";
    }
}
