using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace NorthLand.Combat
{
    // ── 이름 규칙 ──────────────────────────────────────────────────────────
    // 이 파일의 파생 클래스는 **`*Status`**로 끝난다. 스킬 계열(`Skill/`의 `SkillEffect : MonoBehaviour`
    // 파생)이 `*Effect`를 쓰고 있어서, 예전에는 `NorthLand.Combat.BurnEffect`와 전역 `BurnEffect`가
    // 이름으로 충돌했다 — `using NorthLand.Combat`을 걸어도 전역(스킬) 쪽이 이겨서, 타워 화상을
    // 코드로 저작하려 하면 "'BurnEffect'에 'DamagePerTick' 정의가 없다"는 컴파일 에러가 났다.
    //
    //   *Effect  = 스킬 계열 (MonoBehaviour, 플레이어 스킬·웨이브 보상)
    //   *Status  = 타워가 대상에게 거는 상태이상 (HitEffect 파생, TowerAsset.Effects에 담긴다)
    //
    // ⚠ 이 클래스들은 `[SerializeReference]`로 **타입 이름 문자열**이 에셋에 박힌다. 다시 rename하려면
    //   `[MovedFrom]`을 반드시 함께 갱신할 것 — 빠뜨리면 기존 SO의 Effects 항목이 조용히 null이 된다.
    // 이 효과가 무엇인지. 합성 계승(#274 Phase 5)이 "종류"를 물려주는 단위이기도 하다.
    public enum EffectKind
    {
        Burn,
        Poison,
        Slow,
        Stun,
    }

    // 대상에게 거는 효과 한 조각. `TowerAsset.Effects`에 [SerializeReference]로 담겨
    // 인스펙터에서 `+ Burn`, `+ Slow`를 골라 붙이고 그 자리에서 수치를 넣는다(#274).
    //
    // **액션(TowerAction)과 같은 패턴을 한 층 아래에 적용한 것이다:**
    //   타워 → [액션 목록]   "이 타워는 무엇을 하는가"
    //   액션 → [효과 목록]   "맞으면 무슨 일이 나는가"
    //
    // **신규 런타임 인프라가 없다.** 모든 구현체는 기존 StatusEffectHandler로 흘러가고, 효과 소유·
    // 지속시간 소진·소스별 공존은 이미 대상 쪽에 구현돼 있다(Tower.md §5.4). 지금까지 부족했던 건
    // "어떤 효과를 걸지"가 데이터가 아니었다는 것뿐이다.
    //
    // ⚠ 액션과 달리 **수치를 직접 갖는다.** 부품과 수치가 한 덩어리여야 인스펙터에서 고르는 즉시
    // 저작이 끝나기 때문이다. 밸런싱 값이 프리팹으로 내려가지 않도록 `Effects`는 프리팹이 아니라
    // **SO에 산다** — 액션 규칙 ①(수치는 SO)과 어긋나지 않는다.
    [Serializable]
    public abstract class HitEffect
    {
        public abstract EffectKind Kind { get; }

        /// 효과 소스 키 채번 — **명중 경로와 오라 경로가 반드시 이 하나를 공유한다.**
        ///
        /// 같은 종류 타워 여러 기는 `baseId`(타워 인스턴스 ID)가 달라 자동 중첩되고,
        /// 한 타워 안에서는 종류당 하나로 수렴한다.
        ///
        /// ⚠ 예전에는 `baseId ^ (int)kind`였는데 `EffectKind`가 0~3이라 **하위 2비트만 흔들었다.**
        /// 두 타워의 `GetInstanceID()`가 bit0만 달라도 `A^Poison == B^Burn`이 되어,
        /// `StatusEffectHandler`의 `Dictionary<int, DotState>` 슬롯 하나를 두 타워가 공유해
        /// **중첩 대신 갱신만** 됐다 — 한쪽 DoT가 조용히 사라지는 경로다(PR #278 리뷰).
        /// 예전 "감속 타워 2기 → 1중첩" 사고와 같은 실패 유형이라 해시 결합으로 바꿨다.
        public static int SourceKey(int baseId, EffectKind kind) => HashCode.Combine(baseId, (int)kind);

        /// 대상에 효과를 건다.
        ///
        /// `stats`: 소스 타워의 스탯 원장(타워가 아니면 null). DoT 수치가 타일 버프·오라 버프를 타도록
        /// 여기서 합성한다 — 예전에는 오라가 SO 값을 그대로 넘겨 "사거리 타일은 먹히는데 공격력 타일은
        /// 무반응"인 비대칭이 있었다(Tower.md §5.3).
        public abstract void Apply(IDamageable target, IAttacker source, TowerStats stats, int sourceId);

        /// 정보 패널에 이 효과가 기여할 줄. 없으면 null. 실효값(원장 합성 후)을 보여준다.
        public abstract string Describe(TowerStats stats);

        /// 대상의 상태이상 핸들러를 얻는다(없으면 붙인다). 대상이 Component가 아니면 null.
        protected static StatusEffectHandler Resolve(IDamageable target)
        {
            if (target is not Component c) return null;
            return c.TryGetComponent(out StatusEffectHandler handler)
                ? handler
                : c.gameObject.AddComponent<StatusEffectHandler>();
        }
    }

    // ── 지속 피해(DoT) 공통 ────────────────────────────────────────────────
    // 화상·독은 수치만 다르고 거는 방식이 같다. 종류를 나누는 이유는 합성 계승(§9)이 "종류"를
    // 단위로 쓰고, 대상 쪽 슬롯도 종류별로 공존해야 하기 때문이다.
    [Serializable]
    public abstract class DamageOverTimeEffect : HitEffect
    {
        public float DamagePerTick = 5f;
        public float TickInterval = 1f;
        public float Duration = 3f;

        public override void Apply(IDamageable target, IAttacker source, TowerStats stats, int sourceId)
        {
            StatusEffectHandler handler = Resolve(target);
            if (handler == null) return;

            handler.ApplyOrRefresh(sourceId, Kind, ScaledDamage(stats), ScaledTick(stats), Duration, source);
        }

        public override string Describe(TowerStats stats)
            => TowerStatsFormatter.BuildDotLine(ScaledDamage(stats), ScaledTick(stats));

        float ScaledDamage(TowerStats stats)
            => stats == null ? DamagePerTick : stats.Evaluate(TowerStat.AttackDamage, DamagePerTick);

        // 공속이 오를수록 틱 간격이 짧아진다(공격 타워의 AttackInterval과 같은 규칙).
        float ScaledTick(TowerStats stats)
            => stats == null
                ? TickInterval
                : TickInterval / Mathf.Max(stats.Evaluate(TowerStat.AttackSpeed, 1f), 0.01f);
    }

    [Serializable]
    [MovedFrom(true, sourceClassName: "BurnEffect")]
    public sealed class BurnStatus : DamageOverTimeEffect
    {
        public override EffectKind Kind => EffectKind.Burn;
    }

    [Serializable]
    [MovedFrom(true, sourceClassName: "PoisonEffect")]
    public sealed class PoisonStatus : DamageOverTimeEffect
    {
        public override EffectKind Kind => EffectKind.Poison;
    }

    // ── 감속 ───────────────────────────────────────────────────────────────
    [Serializable]
    [MovedFrom(true, sourceClassName: "SlowEffect")]
    public sealed class SlowStatus : HitEffect
    {
        [Tooltip("이동속도 배율. 0.6 = 40% 감속. 0이면 스턴이 되므로 StunStatus를 쓸 것.")]
        [Range(0.01f, 1f)] public float Multiplier = 0.6f;
        public float Duration = 2f;

        public override EffectKind Kind => EffectKind.Slow;

        // 감속 강도는 원장을 거치지 않는다 — TowerStat에 "CC 강도" 축이 없고, 감속을 공격력/공속 축에
        // 억지로 매핑하면 의미가 어긋난다. 축 신설이 선행 과제다(WL-127).
        public override void Apply(IDamageable target, IAttacker source, TowerStats stats, int sourceId)
            => Resolve(target)?.ApplySlow(sourceId, Multiplier, Duration);

        public override string Describe(TowerStats stats)
            => TowerStatsFormatter.BuildSlowLine(Multiplier);
    }

    // ── 스턴 ───────────────────────────────────────────────────────────────
    [Serializable]
    [MovedFrom(true, sourceClassName: "StunEffect")]
    public sealed class StunStatus : HitEffect
    {
        public float Duration = 0.7f;

        [Tooltip("스턴이 끝난 뒤 이 시간 동안 재스턴을 받지 않는다(초). 대상당 스턴 가동률 상한이 " +
                 "지속/(지속+창)이 되므로, 같은 스턴 타워를 여러 기 깔았을 때의 천장을 이 값이 정한다. " +
                 "0이면 천장이 사라져 대수만큼 봉인이 길어진다 — 그 타워가 대상을 스스로 죽이지 못하면 " +
                 "적이 전진하지 못해 밤이 끝나지 않을 수 있다. " +
                 "티어가 섞이면 종료 시각을 결정한(=더 긴) 스턴의 창이 적용된다.")]
        // ⚠ **음수 금지.** `ApplySlow`의 "미지정" 센티널이 `-1f`라(StatusEffectHandler) 음수를 적으면
        //    "창 없음"이 아니라 **핸들러 폴백(1.4초)으로 흐른다** — 창을 없애려던 의도와 정반대로
        //    가동률이 내려간다. `TowerAsset.OnValidate`의 창 경계 검사도 상한만 보므로 음수는 통과시킨다.
        //    `SlowStatus.Multiplier`의 [Range]와 같은 축의 방어다.
        [Min(0f)] public float ImmunityWindow = 0.4f;

        public override EffectKind Kind => EffectKind.Stun;

        // ⚠ **소스 키를 무시하고 공유 static ID를 쓴다.**
        //
        // 다른 효과는 타워 인스턴스별로 채번해 여러 기가 중첩되지만, 스턴은 #274 이전부터 모든 소다
        // 타워가 단일 슬롯(`"onhit.stun"`)을 공유해왔다. **이 공유는 그때부터의 관성이고, 정확성
        // 요건이 아니다.** 티어별로 스턴 지속이 갈린 지금도 "에피소드 시작 기준 천장"을 지키는 것은
        // 공유가 아니라 `StatusEffectHandler.ExtendStun`이 **들어온 effectId가 아니라 `stunSource`
        // 슬롯만 갱신한다는 점**이다 — 스턴 중 재적용은 `ApplySlow`가 `ExtendStun` 호출 후 즉시
        // return해 슬롯 삽입 경로에 닿지 않고, `stunSource`는 새 에피소드에서만 대입된다.
        // 그래서 effectId를 타워별로 갈라도 대상당 스턴 슬롯은 하나다.
        //
        // 인스턴스별 전환(TowerRedesign.md §12 #1)의 남은 쟁점은 **정확성이 아니라 밸런스**다 —
        // 1기 가동률이 대수에 비례해 오르다 면역 창 천장에서 멈추는 형태로 바뀐다.
        // ⚠ 실제로 천장을 깨는 변경은 effectId를 가르는 것이 아니라, `ExtendStun`의 `stunSource`
        //    고정을 "불필요한 우회"로 보고 들어온 effectId로 바꾸는 것이다. 그쪽 주석과 함께 읽을 것.
        static readonly int SharedEffectId = "onhit.stun".GetHashCode();

        public override void Apply(IDamageable target, IAttacker source, TowerStats stats, int sourceId)
        {
            if (Duration <= 0f) return;

            // 배율 0을 넘기면 핸들러가 속도 축이 아니라 스턴 축(IMovementAgent.AddStun)으로 보낸다 —
            // 속도 축은 50% 상대 하한이 있어 배율 0으로도 멈추지 않고 서행한다.
            Resolve(target)?.ApplySlow(SharedEffectId, 0f, Duration, ImmunityWindow);
        }

        public override string Describe(TowerStats stats)
            => Duration > 0f ? $"Stun: {Duration:0.#}s" : null;
    }
}
