using System;
using UnityEngine;

namespace NorthLand.Combat
{
    /// 이번 프레임에 투사체가 무엇을 해야 하는지. **두 플래그가 갈라져 있는 것이 핵심이다.**
    ///
    /// 예전에는 "도달 = 명중 = 소멸"이 한 덩어리로 비행 코드에 하드코딩돼 있어서, 가는 길에 여러 번
    /// 때리는 탄(관통·부메랑)을 표현할 방법이 아예 없었다. 둘을 가르면 이렇게 된다:
    ///
    ///   유도탄·포격    도달 시 Impact + Finished
    ///   대상 소실      Finished만 (명중 없이 사라진다)
    ///   관통탄         적을 지날 때마다 Impact, 사거리 끝에 Finished
    ///   부메랑         왕복하며 여러 번 Impact, 복귀 완료 시 Finished
    public struct FlightStep
    {
        /// 이번 프레임에 명중 판정을 하라. 무엇을 때릴지는 명중 축(ProjectileImpact)이 정한다.
        public bool Impact;

        /// 명중 판정의 기준 위치. Area는 이 지점을 중심으로 스플래시를 굴린다.
        public Vector3 ImpactPos;

        /// 수명 종료 → 호스트가 파괴한다. Impact와 **독립**이다.
        public bool Finished;

        public static FlightStep Flying => default;
        public static FlightStep Expire => new FlightStep { Finished = true };

        public static FlightStep HitAndExpire(Vector3 pos)
            => new FlightStep { Impact = true, ImpactPos = pos, Finished = true };

        /// 때렸지만 계속 난다(관통·부메랑).
        public static FlightStep HitAndContinue(Vector3 pos)
            => new FlightStep { Impact = true, ImpactPos = pos };
    }

    /// 투사체 한 발의 진행 상태. **부품이 아니라 `Projectile`이 소유하고 `ref`로 넘긴다.**
    ///
    /// ⚠ 액션(TowerAction)과 결정적으로 다른 지점이다. 액션은 **프리팹**에 담겨 Instantiate 시
    /// 인스턴스마다 깊은 복사되므로 상태를 가져도 안전했지만, 비행 부품은 **SO**에 담겨
    /// **여러 투사체가 같은 객체를 공유**한다 — 부품에 진행값을 두면 10발이 서로의 값을 덮어쓴다.
    public struct FlightState
    {
        /// 발사 지점.
        public Vector3 Start;

        /// 아크를 얹기 전의 "평면" 위치. 유도탄이 대상을 쫓는 실제 좌표이며,
        /// 화면에 보이는 위치는 여기에 포물선 높이를 더한 값이다.
        public Vector3 Planar;

        /// 발사 순간 고정한 착탄점(포격). 대상이 죽거나 움직여도 안 바뀐다.
        public Vector3 Landing;

        /// 발사 시 시작점→대상 거리. 아크 진행도 t의 기준.
        public float TotalDistance;

        /// 누적 이동 거리.
        public float Traveled;
    }

    /// "어떻게 날아가는가" 한 조각. **타워 SO(`TowerAsset.Attack.Flight`)에 [SerializeReference]로 담긴다** —
    /// 인스펙터에서 종류를 고르면 그 자리에 그 종류의 수치가 함께 뜬다(HitEffect와 같은 패턴).
    ///
    /// 결정하는 쪽은 **타워**이고 투사체는 실행만 한다. 그래서 탄환 프리팹 하나(Rolly_Bullet)를
    /// archer/gatling/sniper/soda가 공유하면서도 각자 다른 속도·궤적을 가질 수 있다.
    ///
    /// ★ **부품은 상태를 갖지 않는다.** SO에 살아 여러 투사체가 공유하므로, 진행값은 전부
    /// `FlightState`에 담아 호스트가 소유한다. 새 비행 방식을 만들 때 이 규칙만 지키면
    /// `Projectile.cs`는 한 글자도 안 바뀐다.
    [Serializable]
    public abstract class ProjectileFlight
    {
        /// 발사 직후 1회. 착탄점 고정·초기 거리 스냅샷처럼 "발사 순간에만" 정해지는 값을 채운다.
        public abstract void Begin(Projectile self, IDamageable target, ref FlightState state);

        /// 이번 프레임 이동. **위치만 정한다** — 기수 회전은 호스트가 이동 방향을 보고 공통으로 처리한다.
        public abstract FlightStep Step(Projectile self, IDamageable target, ref FlightState state, float deltaTime);

        /// 양 끝 0, t=0.5에서 정점인 포물선 높이. **판정에 영향 없는 겉보기 값**이라 위치에만 더한다.
        protected static float Arc(float height, float t) => height * 4f * t * (1f - t);
    }

    /// 살아있는 대상을 매 프레임 추적하는 유도탄. **반드시 명중한다.**
    /// `ArcHeight > 0`이면 평면 추적 위에 포물선을 얹어 곡사처럼 보이게 한다(캐논이 이 조합이다).
    [Serializable]
    public sealed class HomingFlight : ProjectileFlight
    {
        public float Speed = 800f;

        [Tooltip("포물선 정점 높이. 겉보기 전용이며 명중 판정에는 영향이 없다.")]
        public float ArcHeight;

        public override void Begin(Projectile self, IDamageable target, ref FlightState state)
        {
            state.Start = self.transform.position;
            state.Planar = state.Start;

            Transform at = target?.HitPosition;
            state.TotalDistance = at != null ? Vector3.Distance(state.Planar, at.position) : 0f;
        }

        public override FlightStep Step(Projectile self, IDamageable target, ref FlightState state, float deltaTime)
        {
            Transform at = target?.HitPosition;
            if (at == null || target.IsDead)
                return FlightStep.Expire;   // 대상이 도중에 사라지면 명중 없이 소멸

            Vector3 targetPos = at.position;
            state.Planar = Vector3.MoveTowards(state.Planar, targetPos, Speed * deltaTime);

            // 진행도 t: 시작 0 → 근접할수록 1(초기 거리 기준). 대상이 멀어지면 0으로 clamp된다.
            float remaining = Vector3.Distance(state.Planar, targetPos);
            float t = state.TotalDistance > 0.0001f
                ? Mathf.Clamp01(1f - remaining / state.TotalDistance)
                : 1f;

            self.transform.position = state.Planar + Vector3.up * Arc(ArcHeight, t);

            return remaining < 0.1f ? FlightStep.HitAndExpire(targetPos) : FlightStep.Flying;
        }
    }

    /// 발사 순간 대상 위치를 착탄점으로 **고정**하고 그 지점까지 난다.
    /// 대상이 죽거나 움직여도 고정된 지점에 그대로 명중하므로 **빗나갈 수 있다** —
    /// 광역(Area)과 짝지을 때 "적 무리의 길목을 예측해 쏜다"가 성립한다.
    [Serializable]
    public sealed class BallisticFlight : ProjectileFlight
    {
        public float Speed = 100f;

        [Tooltip("포물선 정점 높이. 겉보기 전용이며 명중 판정에는 영향이 없다.")]
        public float ArcHeight;

        public override void Begin(Projectile self, IDamageable target, ref FlightState state)
        {
            state.Start = self.transform.position;

            Transform at = target?.HitPosition;
            state.Landing = at != null ? at.position : state.Start;
            state.TotalDistance = Vector3.Distance(state.Start, state.Landing);
        }

        public override FlightStep Step(Projectile self, IDamageable target, ref FlightState state, float deltaTime)
        {
            state.Traveled += Speed * deltaTime;
            float t = state.TotalDistance > 0.0001f
                ? Mathf.Clamp01(state.Traveled / state.TotalDistance)
                : 1f;

            Vector3 pos = Vector3.Lerp(state.Start, state.Landing, t);
            pos.y += Arc(ArcHeight, t);
            self.transform.position = pos;

            return t >= 1f ? FlightStep.HitAndExpire(state.Landing) : FlightStep.Flying;
        }
    }
}
