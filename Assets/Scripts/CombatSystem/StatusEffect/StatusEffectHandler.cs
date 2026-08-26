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
            // 어떤 종류의 DoT인지(#502). **effectId로는 복원할 수 없어서** 따로 들고 있어야 한다 —
            // effectId는 `HitEffect.SourceKey(baseId, kind)` = `HashCode.Combine(...)`라 되돌릴 수 없다.
            public EffectKind kind;

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
        //  1) **에피소드 시작 기준 천장** — 스턴 중에 들어온 재적용은 종료 시각을
        //     `에피소드 시작 + 이번 지속`까지만 끌어올릴 수 있다(**지금 시각 기준이 아니다**).
        //     따라서 한 에피소드의 길이는 저작된 스턴 지속의 **최댓값**을 넘지 못하고, 같은 타워가
        //     아무리 도배해도 후보가 계속 줄어들어 remaining이 다시 채워지지 않는다.
        //     **이 상한은 지우면 안 된다** — 밤 종료 조건이 몬스터 전멸이라(MonsterSpawn)
        //     영구 정지는 곧 밤이 끝나지 않는 것이다.
        //  2) 종료 후 면역 창 — 1)만으로는 만료 직후 재스턴이 가능해 가동률이 100%에 가깝다.
        // 결과적으로 최대 가동률 = 스턴 지속 / (스턴 지속 + 면역 창)이 되어 타워를 몇 기 깔든 상한이 있다.
        //
        // ⚠ 1)은 원래 "스턴 중 재적용 **무시**"였다. 티어별로 스턴 지속이 다른 스턴 타워가 생기면서
        //   (1티어 0.7s / 2티어 1.0s) 그 규칙이 틀린 답을 냈다 — 둘이 거의 동시에 맞히면 먼저 도착한
        //   1티어가 슬롯을 잡고 2티어 명중이 통째로 버려져 총 0.7초가 됐다. 상한을 "무시"가 아니라
        //   "에피소드 시작 기준 천장"으로 표현하면 더 긴 스턴이 늦게 와도 총 1.0초가 되면서
        //   소프트락 방어(반복 명중으로 늘어나지 않음)는 그대로 유지된다.
        //
        // ⚠ 두 규칙의 역할이 #441에서 갈렸다. **1기 기준 가동률은 공격 간격이 정한다**
        //   (`간격 > 스턴 지속`이면 매 발이 새 스턴이 되고 가동률 = 지속/간격 — TowerAsset.OnValidate가
        //   이 하한을 경고로 지킨다). 2)는 이제 **다기(多機) 케이스 전용 상한**이고, 창 길이는 부여자가
        //   저작한다 — 소다를 여럿 깔았을 때 기여가 0이 되지 않으면서(WL-141) 완전 봉인도 막는 손잡이다.
        //
        // 소스가 아니라 대상 기준으로 판정하는 이유: 소스 기준이면 서로 다른 스턴원 2개가 번갈아 걸어
        // 다시 영구 정지가 만들어진다. "행동 불가"는 겹칠 이유가 없으므로 대상당 에피소드는 하나뿐이고,
        // 슬롯도 `stunSource` 하나만 쓴다 — 들어온 effectId가 달라도 그 슬롯을 끌어올린다.
        //
        // ⚠ **이 값은 이제 폴백이다**(#441). 부여자가 창 길이를 함께 넘기면 그 값이 이긴다 —
        // 스턴을 거는 유일한 경로인 `StunStatus`가 SO 필드로 창을 저작하게 됐으므로(WL-026: CC 가동률
        // 상한이 코드 기본값에 갇혀 있던 문제), 실제 플레이에서 아래 값이 쓰이는 경로는 남지 않는다.
        // 핸들러를 미리 부착해 두고 ApplySlow를 직접 부르는 호출자(테스트 등)를 위해 남긴다.
        [SerializeField] float stunImmunityWindow = 1.4f;

        // 이 시각 전에는 재스턴을 받지 않는다. 스턴이 끝나는 시점에 갱신된다.
        float stunImmuneUntil;

        // 지금 걸린 스턴이 끝날 때 적용할 창. **종료 시각을 결정한 스턴의 값**을 EndStun까지 들고 간다 —
        // 만료 시점에 조회하면 그 사이 다른 부여자가 값을 바꿨을 때 어느 스턴의 규칙인지가 흐려진다.
        // 에피소드 도중 더 긴 스턴이 천장을 끌어올리면(규칙 1) 그 스턴의 창으로 교체된다.
        float activeStunImmunity;

        // 현재 스턴 에피소드가 시작된 시각. 규칙 1의 천장(`stunEpisodeStart + duration`) 기준점이다.
        // stunActive가 false면 의미 없다.
        float stunEpisodeStart;

        // 현재 스턴을 보유한 소스. stunActive가 false면 의미 없다
        // (effectId는 해시코드라 -1 같은 센티널을 쓸 수 없다).
        bool stunActive;
        int stunSource;

        // ── 표시용 조회(#502) ──────────────────────────────────────
        // 상태이상 아이콘 UI가 "지금 뭐가 걸려 있나"를 읽는 유일한 창구다. 이 클래스는 효과를
        // 소유만 하고 그리지 않으므로, 밖으로 나가는 것은 **종류 집합 하나**뿐이다.
        //
        // ⚠ **캐시하지 않고 매번 센다.** 항목이 많아야 서너 개라 세는 비용이 무의미한 반면,
        //   캐시는 만료·사망·감속으로 덮임·전량 해제 네 경로에서 각각 갱신을 빠뜨릴 수 있고
        //   그 실패는 "아이콘이 안 꺼진다"로만 보여 원인에서 멀다.

        /// 종류 하나에 해당하는 비트. 비트 i = `1 << (int)EffectKind`.
        public static int MaskOf(EffectKind kind) => 1 << (int)kind;

        /// 지금 걸려 있는 상태이상 종류의 비트마스크. 아무것도 없으면 0.
        public int ActiveKindMask
        {
            get
            {
                int mask = 0;

                foreach (var kv in effects)
                {
                    mask |= MaskOf(kv.Value.kind);
                }

                // 감속·스턴은 배율이 종류를 정한다 — 0이면 스턴 축(AddStun), 그 위면 감속 축이다.
                foreach (var kv in slows)
                {
                    mask |= MaskOf(kv.Value.multiplier <= 0f ? EffectKind.Stun : EffectKind.Slow);
                }

                return mask;
            }
        }

        public bool Has(EffectKind kind) => (ActiveKindMask & MaskOf(kind)) != 0;

        /// 이 종류가 지금 **몇 겹** 걸려 있는가(#502 스택 표시). 겹의 단위는 **소스(`effectId`)**다 —
        /// 같은 타워가 다시 맞혀도 그건 갱신이라 늘지 않고, 다른 타워가 걸면 늘어난다.
        ///
        /// 왜 겹의 수가 곧 세기인가:
        /// · DoT는 소스마다 자기 틱을 독립으로 돌리므로 겹칠수록 **초당 피해가 더해진다.**
        /// · 감속은 `MoveSpeedComposer`가 소스별 배율을 **곱하므로** 겹칠수록 느려진다(`PushToMover` 주석).
        /// · 스턴은 대상당 에피소드가 하나뿐이라(`stunSource` 슬롯 1개) 항상 0 또는 1이다.
        public int CountOf(EffectKind kind)
        {
            int count = 0;

            foreach (var kv in effects)
            {
                if (kv.Value.kind == kind)
                {
                    count++;
                }
            }

            foreach (var kv in slows)
            {
                if ((kv.Value.multiplier <= 0f ? EffectKind.Stun : EffectKind.Slow) == kind)
                {
                    count++;
                }
            }

            return count;
        }

        void Awake()
        {
            owner = GetComponent<IDamageable>();
            // mover는 자식 GO까지 탐색한다(WL-129 A안). Enemy.cs·MonsterSpawn·MonsterStateMachine의
            // GetComponentInChildren 탐색과 정합 — MonsterMove가 자식에 있어도 CC(슬로우/스턴)가 적용된다.
            mover = GetComponentInChildren<IMovementAgent>();
            activeStunImmunity = stunImmunityWindow;
        }

        // 타워가 사거리 내에서 매 Interval마다 호출.
        // 이미 있으면 남은 지속시간을 duration으로 리셋(갱신), 없으면 새로 추가한다.
        //
        // `kind`는 피해 계산에 쓰이지 않는다 — **표시용**이다(#502의 상태이상 아이콘이 읽는
        // `ActiveKindMask`). 호출부가 이미 아는 값이라 인자로 받는 것이 가장 싸다.
        public void ApplyOrRefresh(int effectId, EffectKind kind, float damagePerTick, float tickInterval, float duration, IAttacker source)
        {
            if (duration <= 0f || tickInterval <= 0f) return;

            if (effects.TryGetValue(effectId, out var e))
            {
                // 갱신: 남은 시간만 리셋. 틱 리듬(tickTimer)은 유지 → 갱신마다 틱이 리셋되지 않음.
                e.remaining = duration;
                e.damagePerTick = damagePerTick;
                e.tickInterval = tickInterval;
                e.source = source;
                e.kind = kind;
            }
            else
            {
                effects[effectId] = new DotState
                {
                    kind = kind,
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
        /// `immunityWindow` — 이 스턴이 끝난 뒤 재스턴을 막을 시간(초). 음수면 이 핸들러의 폴백을 쓴다.
        /// 감속에는 의미가 없다(감속은 하한 클램프가 받아내므로 게이트를 두지 않는다).
        public void ApplySlow(int effectId, float multiplier, float duration, float immunityWindow = -1f)
        {
            if (duration <= 0f) return;
            multiplier = Mathf.Clamp01(multiplier);

            bool isStun = multiplier <= 0f;

            // 스턴만 게이트한다 — 감속은 하한 클램프가 받아내므로 갱신이 이어져도 소프트락되지 않는다.
            if (isStun)
            {
                // 이미 스턴 중이면 새 에피소드를 열지 않고 현재 에피소드의 천장만 끌어올린다(규칙 1).
                if (stunActive)
                {
                    ExtendStun(duration, immunityWindow);
                    return;
                }

                if (Time.time < stunImmuneUntil)   // 규칙 2
                {
                    if (debugLog)
                        Debug.Log($"[Status] {name}: 스턴 무시 (면역 창 {stunImmuneUntil - Time.time:F2}s 남음)");
                    return;
                }

                stunEpisodeStart = Time.time;
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
                activeStunImmunity = immunityWindow >= 0f ? immunityWindow : stunImmunityWindow;
            }

            PushToMover(effectId, multiplier);
        }

        // 규칙 1. 에피소드 도중 들어온 재스턴을 처리한다 — 종료 시각을 `에피소드 시작 + duration`까지만
        // 끌어올린다. **지금 시각 기준이 아니다**: 시작 기준이라 명중이 반복될수록 후보가 줄어들어
        // 천장을 넘지 못하고, 에피소드 길이가 저작된 스턴 지속의 최댓값으로 묶인다.
        //
        // 들어온 effectId가 아니라 `stunSource` 슬롯을 갱신한다 — 나중에 티어별로 effectId를 갈라도
        // "대상당 스턴 에피소드는 하나"라는 불변식이 유지된다(슬롯이 둘로 갈리면 합집합이 되어 천장이 사라진다).
        // **천장을 지키는 것은 이 한 줄이다** — `HitEffect.StunStatus`가 공유 static ID를 쓰는 것은
        // #274 이전부터의 관성일 뿐 정확성 요건이 아니다(그쪽 주석 참조). 여기를 `effectId`로 바꾸면
        // 그 순간 슬롯이 갈려 천장이 사라진다.
        void ExtendStun(float duration, float immunityWindow)
        {
            if (!slows.TryGetValue(stunSource, out var current))
            {
                // 도달 불가 — 에피소드와 슬롯은 함께 생기고 함께 사라진다. 그래도 어긋났다면
                // 이 대상이 영구히 스턴 면역이 되지 않도록 에피소드를 닫는다.
                EndStun();
                return;
            }

            float candidate = stunEpisodeStart + duration - Time.time;
            if (candidate <= current.remaining)
            {
                if (debugLog)
                    Debug.Log($"[Status] {name}: 스턴 연장 무시 (후보 {candidate:F2}s ≤ 남은 {current.remaining:F2}s)");
                return;
            }

            current.remaining = candidate;
            // 창은 **종료를 결정한 스턴**의 값을 따른다 — 방금 천장을 끌어올린 그 스턴이다.
            activeStunImmunity = immunityWindow >= 0f ? immunityWindow : stunImmunityWindow;

            if (debugLog)
                Debug.Log($"[Status] {name}: 스턴 연장 → 남은 {current.remaining:F2}s (창 {activeStunImmunity:F2}s)");
        }

        // 스턴이 끝나는 모든 경로가 여기를 지나야 한다 — 만료, 감속으로 덮임, 대상 사망.
        void EndStun()
        {
            stunActive = false;
            stunImmuneUntil = Time.time + activeStunImmunity;
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
            stunEpisodeStart = 0f;
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
