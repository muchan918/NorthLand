using System;
using UnityEngine;

namespace NorthLand.Combat
{
    // 스택을 얻는 계기. **수치가 아니라 계기만** 고른다 — 무엇이 얼마나 오르는지는 RampFields가 정한다.
    public enum RampTrigger
    {
        // 이 타워가 쏜 투사체가 명중할 때마다 +1. `Projectile.DamageDealt` 구독.
        // ⚠ 히트스캔(빔)은 그 이벤트를 발행하지 않으므로 이 계기로는 영영 쌓이지 않는다
        //    (TowerAddGuide.md §6에 설계 의도로 명문화됨). TowerAsset.OnValidate가 조합을 경고한다.
        Hit,

        // 이 타워가 마지막 피해를 준 적이 죽을 때마다 +1. `Enemy.Killed` 구독.
        // DoT(화상·독) 처치도 소스가 실려 오므로 귀속된다. 스킬 처치는 소스가 null이라 귀속되지 않는다.
        Kill,
    }

    /// 램프업(성장)의 **수치 묶음**. 스택을 어떻게 세는지는 소비처가 정하고, 이 부품은
    /// "스택 → 배율" 환산과 상한만 소유한다. 그래서 계기가 전혀 다른 두 축이 같은 수치 규약을 공유한다:
    ///
    ///   `RampAction`   — 명중·처치 **이벤트**로 스택을 센다 → 타워 전역 원장(`TowerStats`)에 얹는다
    ///   `BeamFields`   — 한 대상을 잠근 **경과 시간**으로 스택을 센다 → 그 대상에게만 곱한다
    ///
    /// ⚠ 두 소비처가 갈리는 이유는 **적용 범위**다. 원장은 타워 단위라 "대상이 바뀌면 리셋"을
    /// 표현할 수 없다 — 대상별 램프를 원장에 넣으면 다른 액션과 DoT까지 함께 강해지고, 목표를
    /// 바꿔도 배율이 유지된다. 수치 규약만 공유하고 적용 지점은 나누는 것이 이 부품의 요지다.
    ///
    /// `TowerAction`과 달리 **수치를 직접 갖는다** — `HitEffect`와 같은 이유로,
    /// 부품과 수치가 한 덩어리여야 인스펙터에서 고르는 즉시 저작이 끝난다. 프리팹이 아니라
    /// **SO에 사는 값**이므로 액션 규칙 ①(수치는 SO)과 어긋나지 않는다.
    [Serializable]
    public class RampProfile
    {
        [Tooltip("스택 1개당 보너스. 0.05 = 스택당 +5%. 음수면 스택이 쌓일수록 약해진다.")]
        public float PerStack;

        [Tooltip("스택 상한. 0이면 램프가 없는 것과 같다(미저작 판정).")]
        public int MaxStacks;

        [Tooltip("시간 기반 램프에서 스택 1개를 얻는 데 걸리는 초. 이벤트 기반(명중·처치)에서는 쓰이지 않는다.")]
        public float StackInterval;

        [Tooltip("마지막 스택 획득 후 이 시간마다 스택이 1씩 빠진다. 0이면 감쇠 없음(영구 성장).")]
        public float DecaySeconds;

        /// 저작이 갖춰졌는가. `MaxStacks<=0`이나 `PerStack==0`이면 스택을 아무리 쌓아도 배율이 1이라
        /// **예외도 경고도 없이 아무 일이 안 난다** — 소비처는 이 값을 보고 아예 동작을 접는다
        /// (`AttackAction.canFire`와 같은 축: 무동작이면 비용도 0).
        public bool IsAuthored => PerStack != 0f && MaxStacks > 0;

        public int Clamp(int stacks) => stacks < 0 ? 0 : (stacks > MaxStacks ? MaxStacks : stacks);

        /// 스택 → 실효 배율. 상한을 넘겨도 안전하다.
        public float Multiplier(int stacks) => 1f + Clamp(stacks) * PerStack;

        /// 경과 시간 → 스택(시간 기반 램프 전용). `StackInterval`이 0 이하면 시간으로는 쌓이지 않는다.
        public int StacksFromTime(float seconds)
            => StackInterval <= 0f ? 0 : Clamp(Mathf.FloorToInt(seconds / StackInterval));
    }
}
