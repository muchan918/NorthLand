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
        //
        // ⚠ 아래 두 nested 클래스는 **살아 있는 상태**(대상이 소유)이고, 타워 SO에 담기는
        //   `BurnStatus`/`SlowStatus` 등은 **저작 명세**다. 역할이 달라 이름을 `*State`로 갈라둔다 —
        //   예전에 nested 이름이 `SlowEffect`여서 이 파일 안에서 `NorthLand.Combat.SlowEffect`를
        //   가리고 있었다.
        readonly Dictionary<int, DotState> effects = new Dictionary<int, DotState>();
        readonly List<int> expiredBuffer = new List<int>();

        class DotState
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
        readonly Dictionary<int, SlowState> slows = new Dictionary<int, SlowState>();
        readonly List<int> expiredSlowBuffer = new List<int>();

        class SlowState
        {
            public float multiplier;  // 0=스턴, 0.6=40%감속, 1=정상
            public float remaining;   // 남은 지속시간(초)
        }

        // ── 스턴 재적용 제한(#164) ─────────────────────────────
        // 스턴 축은 minMoveSpeed 하한 클램프를 우회해 완전 정지를 만든다. 그래서 클램프가 막고 있던
        // 소프트락을 이 클래스가 대신 막아야 한다. 규칙 두 개가 세트로 필요하다:
        //  1) 스턴 중 재적용 무시 — 없으면 명중마다 remaining이 0.7로 리셋돼 만료가 영원히 오지 않는다.
        //     soda는 AttackInterval 3 / StunDuration 0.7이고 Projectile의 StunEffectId가 static이라
        //     모든 소다가 단일 소스를 공유한다. 5기가 균등 분산되면 명중 간격 0.6s < 0.7s로 끊기지 않는다.
        //  2) 종료 후 면역 창 — 1)만으로는 만료 직후 재스턴이 가능해 가동률이 100%에 가깝다.
        // 결과적으로 최대 가동률 = 스턴 지속 / (스턴 지속 + 면역 창)이 되어 타워를 몇 기 깔든 상한이 있다.
        //
        // 소스가 아니라 대상 기준으로 판정하는 이유: 소스 기준이면 서로 다른 스턴원 2개가 번갈아 걸어
        // 다시 영구 정지가 만들어진다. "행동 불가"는 겹칠 이유가 없으므로 동시에 하나만 허용한다.
        //
        // 이 핸들러는 런타임 AddComponent로 붙으므로(클래스 주석 참조) 실제로는 아래 기본값이 쓰인다.
        // 인스펙터 노출은 미리 부착된 경우의 튜닝용이고, 정식 수치는 GDD §7 CC 사인오프 대상이다(WL-026).
        [SerializeField] float stunImmunityWindow = 1.4f;

        // 이 시각 전에는 재스턴을 받지 않는다. 스턴이 끝나는 시점에 갱신된다.
        float stunImmuneUntil;

        // 현재 스턴을 보유한 소스. stunActive가 false면 의미 없다
        // (effectId는 해시코드라 -1 같은 센티널을 쓸 수 없다).
        bool stunActive;
        int stunSource;

        void Awake()
        {
            owner = GetComponent<IDamageable>();
            // mover는 자식 GO까지 탐색한다(WL-129 A안). Enemy.cs·MonsterSpawn·MonsterStateMachine의
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
                effects[effectId] = new DotState
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

            bool isStun = multiplier <= 0f;

            // 스턴만 게이트한다 — 감속은 하한 클램프가 받아내므로 갱신이 이어져도 소프트락되지 않는다.
            if (isStun && !CanStunNow())
            {
                if (debugLog)
                    Debug.Log($"[Status] {name}: 스턴 무시 ({(stunActive ? "이미 스턴 중" : $"면역 창 {stunImmuneUntil - Time.time:F2}s 남음")})");
                return;
            }

            if (slows.TryGetValue(effectId, out var s))
            {
                // 스턴이 감속으로 덮여 끝나는 경로. 여기서 면역 창을 시작하지 않으면 스턴이
                // 조용히 사라지면서 상한도 함께 사라진다.
                if (!isStun && stunActive && effectId == stunSource) EndStun();

                s.multiplier = multiplier;
                s.remaining = duration;
            }
            else
            {
                slows[effectId] = new SlowState { multiplier = multiplier, remaining = duration };
            }

            if (isStun)
            {
                stunActive = true;
                stunSource = effectId;
            }

            PushToMover(effectId, multiplier);
        }

        // 규칙 1(스턴 중 무시) + 규칙 2(종료 후 면역 창). 위 필드 주석 참조.
        bool CanStunNow() => !stunActive && Time.time >= stunImmuneUntil;

        // 스턴이 끝나는 모든 경로가 여기를 지나야 한다 — 만료, 감속으로 덮임, 대상 사망.
        void EndStun()
        {
            stunActive = false;
            stunImmuneUntil = Time.time + stunImmunityWindow;
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

            // 사망 경로다. EndStun()이 아니라 직접 초기화하는 이유: 면역 창을 남기면 풀링
            // 재사용체가 스턴 안 걸리는 상태로 부활한다.
            stunActive = false;
            stunImmuneUntil = 0f;
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
                    int expiredId = expiredSlowBuffer[i];

                    slows.Remove(expiredId);
                    ReleaseFromMover(expiredId);

                    // 스턴이 자연 만료된 지점. 여기서 면역 창이 시작된다.
                    if (stunActive && expiredId == stunSource) EndStun();
                }
            }
        }
    }
}
