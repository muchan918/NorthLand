using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;

namespace NorthLand.Combat
{
    // IDamageable(몬스터 등)에 부착되어 지속시간형 상태이상(현재 DoT)을 "소유"하고 소진한다.
    // 타워는 사거리 안에서 ApplyOrRefresh만 호출하고, 실제 지속시간 카운트다운과 틱 데미지는
    // 여기(대상 쪽)에서 독립적으로 진행된다 → 대상이 타워 사거리를 벗어나 갱신이 끊겨도
    // 남은 시간만큼 효과가 계속 흐르다가 만료된다.
    //
    // 런타임에 필요할 때 AddComponent로 붙는다(적 프리팹을 미리 수정할 필요 없음).
    // 공용 코드는 인터페이스(IDamageable/IAttacker/DamageInfo)만 참조한다 — 수정 없음.
    // TODO(pooling): 오브젝트 풀링 도입 시 OnEnable에서 effects를 초기화해 재사용체가
    //                이전 생명주기의 디버프를 물려받지 않도록 해야 한다.
    public class StatusEffectHandler : MonoBehaviour
    {
        IDamageable owner;

        public bool debugLog;   // 검증용: 적용/갱신/틱/만료를 Console에 출력

        // effectId당 하나. 같은 소스(같은 타워)는 갱신(refresh), 다른 소스는 공존.
        readonly Dictionary<int, DotEffect> effects = new Dictionary<int, DotEffect>();
        readonly List<int> expiredBuffer = new List<int>();

        class DotEffect
        {
            public float damagePerTick;
            public float tickInterval;
            public float remaining;   // 남은 지속시간(초)
            public float tickTimer;   // 다음 틱까지 남은 시간(초)
            public IAttacker source;
        }

        // 이동속도 배율을 세팅할 대상(MonsterMove 등). 없으면 슬로우/스턴은 무시된다.
        IMovementAgent mover;

        // 슬로우/스턴(#164). effectId당 하나(같은 소스=갱신). 여러 개면 가장 강한 감속(최소 배율)을 적용.
        readonly Dictionary<int, SlowEffect> slows = new Dictionary<int, SlowEffect>();
        readonly List<int> expiredSlowBuffer = new List<int>();

        class SlowEffect
        {
            public float multiplier;  // 0=스턴, 0.6=40%감속, 1=정상
            public float remaining;   // 남은 지속시간(초)
        }

        void Awake()
        {
            owner = GetComponent<IDamageable>();
            // mover는 자식 GO까지 탐색한다(WL-111 A안). Enemy.cs·MonsterSpawn·MonsterStateMachine의
            // GetComponentInChildren 탐색과 정합 — MonsterMove가 자식에 있어도 CC(슬로우/스턴)가 적용된다.
            mover = GetComponentInChildren<IMovementAgent>();
        }

        // 타워가 사거리 내에서 매 Interval마다 호출.
        // 이미 있으면 남은 지속시간을 duration으로 리셋(갱신), 없으면 새로 추가한다.
        public void ApplyOrRefresh(int effectId, float damagePerTick, float tickInterval, float duration, IAttacker source)
        {
            if (duration <= 0f || tickInterval <= 0f) return;

            if (effects.TryGetValue(effectId, out var e))
            {
                // 갱신: 남은 시간만 리셋. 틱 리듬(tickTimer)은 유지 → 갱신마다 틱이 리셋되지 않음.
                e.remaining = duration;
                e.damagePerTick = damagePerTick;
                e.tickInterval = tickInterval;
                e.source = source;
            }
            else
            {
                effects[effectId] = new DotEffect
                {
                    damagePerTick = damagePerTick,
                    tickInterval = tickInterval,
                    remaining = duration,
                    tickTimer = tickInterval,   // 첫 틱은 1주기 뒤 (진입 즉시 버스트 방지)
                    source = source,
                };
            }
        }

        // 타워/투사체가 호출: 대상에 슬로우(또는 스턴=배율0)를 부여·갱신한다. DoT와 동일하게 대상 쪽에서 duration을 소진 →
        // 갱신이 끊겨도 남은 시간 후 원복. multiplier: 1=정상, 0.6=40%감속, 0=완전정지. 같은 effectId는 갱신(존 재적용/재명중).
        public void ApplySlow(int effectId, float multiplier, float duration)
        {
            if (duration <= 0f) return;
            multiplier = Mathf.Clamp01(multiplier);

            if (slows.TryGetValue(effectId, out var s))
            {
                s.multiplier = multiplier;
                s.remaining = duration;
            }
            else
            {
                slows[effectId] = new SlowEffect { multiplier = multiplier, remaining = duration };
            }
            RecomputeSlow();
        }

        // 활성 슬로우/스턴 중 가장 강한 것(최소 배율)을 이동 에이전트에 적용. 없으면 1(원복).
        void RecomputeSlow()
        {
            float m = 1f;
            foreach (var s in slows.Values)
                if (s.multiplier < m) m = s.multiplier;

            mover?.SetSlowMultiplier(m);
            if (debugLog) Debug.Log($"[Status] {name}: 슬로우 배율={m:F2} (활성 {slows.Count}개)");
        }

        void Update()
        {
            // 대상 사망/유실: 모든 효과 정리 + 슬로우 배율 원복(다음 재사용체 대비)
            if (owner == null || owner.IsDead)
            {
                if (effects.Count > 0) effects.Clear();
                if (slows.Count > 0) { slows.Clear(); mover?.SetSlowMultiplier(1f); }
                return;
            }

            float dt = Time.deltaTime;

            // --- DoT ---
            if (effects.Count > 0)
            {
                expiredBuffer.Clear();
                foreach (var kv in effects)
                {
                    var e = kv.Value;
                    e.remaining -= dt;
                    e.tickTimer -= dt;

                    // dt가 커서 한 프레임에 여러 틱이 밀렸을 경우까지 처리
                    while (e.tickTimer <= 0f)
                    {
                        owner.TakeDamage(new DamageInfo(e.damagePerTick, e.source));
                        if (debugLog) Debug.Log($"[Status] {name}: DoT 틱 -{e.damagePerTick}, 남은시간={Mathf.Max(e.remaining, 0f):F2}s");
                        e.tickTimer += e.tickInterval;
                        if (owner.IsDead) break;
                    }

                    if (owner.IsDead)
                    {
                        effects.Clear();
                        if (slows.Count > 0) { slows.Clear(); mover?.SetSlowMultiplier(1f); }
                        return;
                    }
                    if (e.remaining <= 0f) expiredBuffer.Add(kv.Key);
                }
                for (int i = 0; i < expiredBuffer.Count; i++)
                    effects.Remove(expiredBuffer[i]);
            }

            // --- 슬로우/스턴 ---
            if (slows.Count > 0)
            {
                expiredSlowBuffer.Clear();
                foreach (var kv in slows)
                {
                    kv.Value.remaining -= dt;
                    if (kv.Value.remaining <= 0f) expiredSlowBuffer.Add(kv.Key);
                }
                if (expiredSlowBuffer.Count > 0)
                {
                    for (int i = 0; i < expiredSlowBuffer.Count; i++)
                        slows.Remove(expiredSlowBuffer[i]);
                    RecomputeSlow();   // 만료분 제거 후 재계산(남은 게 없으면 1로 원복)
                }
            }
        }
    }
}
