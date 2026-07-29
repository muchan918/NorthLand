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
            PushToMover(effectId, multiplier);
        }

        // 효과 하나를 이동 에이전트의 해당 축에 밀어넣는다. 여러 효과를 여기서 합치지 않는 이유:
        // MoveSpeedComposer가 소스별 곱산으로 이미 합성하므로(#233), 예전처럼 최소 배율 하나만
        // 골라 보내면 두 번째 감속 타워가 조용히 무시된다.
        //
        // 매번 두 축을 함께 지시하는 이유: 같은 effectId가 갱신될 때 감속↔스턴으로 성격이 바뀔 수 있어
        // (데이터 수정, 같은 타워의 impact 재사용), 반대 축에 남은 잔재를 그 시점에 걷어내야 한다.
        void PushToMover(int effectId, float multiplier)
        {
            if (mover == null) return;

            // 배율 0은 스턴 축으로 보낸다 — 속도 축은 minMoveSpeed 하한 클램프가 걸려 있어
            // 배율 0을 넣어도 멈추지 않고 서행한다(StunGate 주석 참조).
            if (multiplier <= 0f)
            {
                mover.RemoveSpeedDebuff(effectId);
                mover.AddStun(effectId);
            }
            else
            {
                mover.RemoveStun(effectId);
                mover.AddSpeedDebuff(effectId, multiplier);
            }

            if (debugLog)
                Debug.Log($"[Status] {name}: {(multiplier <= 0f ? "스턴" : $"감속 배율={multiplier:F2}")} 적용 (활성 {slows.Count}개)");
        }

        // 만료·사망 시 두 축에서 모두 걷어낸다 — 어느 축에 들어갔는지 따로 기억하지 않고,
        // 없는 sourceId 제거는 양쪽 다 무해하므로 분기 없이 둘 다 호출한다.
        void ReleaseFromMover(int effectId)
        {
            if (mover == null) return;

            mover.RemoveSpeedDebuff(effectId);
            mover.RemoveStun(effectId);
        }

        // 대상 사망/유실 시 전량 해제. 이동 에이전트는 대상과 생명주기가 같지만, 풀링 도입 시
        // 재사용체가 이전 생명주기의 감속·스턴을 물려받지 않으려면 여기서 확실히 비워야 한다.
        void ReleaseAllSlows()
        {
            if (slows.Count == 0) return;

            foreach (int effectId in slows.Keys)
                ReleaseFromMover(effectId);

            slows.Clear();
        }

        void Update()
        {
            // 대상 사망/유실: 모든 효과 정리 + 슬로우 배율 원복(다음 재사용체 대비)
            if (owner == null || owner.IsDead)
            {
                if (effects.Count > 0) effects.Clear();
                ReleaseAllSlows();
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
                        ReleaseAllSlows();
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
                for (int i = 0; i < expiredSlowBuffer.Count; i++)
                {
                    // 만료분만 해제한다 — 남은 효과는 컴포저·스턴 게이트가 그대로 유지하므로
                    // 예전처럼 전체를 재계산할 필요가 없다.
                    slows.Remove(expiredSlowBuffer[i]);
                    ReleaseFromMover(expiredSlowBuffer[i]);
                }
            }
        }
    }
}
