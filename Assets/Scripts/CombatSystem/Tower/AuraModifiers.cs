using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // TowerAsset의 StatModifier(저작 형식)를 실제 적용 값으로 환산하는 순수 함수 모음.
    //
    // 별도 클래스로 뽑은 이유: 같은 환산식이 적용부(오라 행동)와 표시부(정보 패널·툴팁)에서 각자 쓰이면
    // "패널엔 -40%인데 실제론 -30%" 같은 어긋남이 생긴다(WL-130이 지적한 가드/적용 비대칭과 같은 축).
    // MonoBehaviour가 아니고 Unity 상태를 읽지 않으므로 EditMode 테스트로 검증할 수 있다.
    public static class AuraModifiers
    {
        // 공통 환산 규칙: IsPercentage면 Amount를 %로(25 → ×1.25), 아니면 배율 가산분으로(0.25 → ×1.25).
        static float ToMultiplier(StatModifier modifier)
            => modifier.IsPercentage ? 1f + modifier.Amount / 100f : 1f + modifier.Amount;

        /// MoveSpeed modifier들을 곱연산 슬로우 배율로 환산한다.
        /// 감속만 의미가 있으므로 [0,1]로 제한한다(양수 Amount로 적을 가속시키지 않음). 결과 1 = 감속 없음.
        public static float ComputeSlowMultiplier(List<StatModifier> modifiers)
        {
            float result = 1f;
            if (modifiers == null) return result;

            for (int i = 0; i < modifiers.Count; i++)
            {
                StatModifier modifier = modifiers[i];
                if (modifier == null || modifier.Stat != ModifiableStat.MoveSpeed) continue;

                result *= ToMultiplier(modifier);
            }

            return Mathf.Clamp01(result);
        }

        /// 아군 타워에 부여할 modifier들을 원장 형식으로 환산해 dst에 채운다.
        ///
        /// AttackDamage/AttackSpeed만 반영한다 — MoveSpeed/Armor는 적 스탯이라 타워에 의미가 없다.
        /// (저작에 그 둘을 넣어도 조용히 무시되는 문제는 authoring-time 검증 안건으로 남아 있다 — WL-130)
        public static void ConvertBuffModifiers(List<StatModifier> modifiers, List<TowerStatModifier> dst)
        {
            dst.Clear();
            if (modifiers == null) return;

            float damageMultiplier = 1f;
            float attackSpeedMultiplier = 1f;

            for (int i = 0; i < modifiers.Count; i++)
            {
                StatModifier modifier = modifiers[i];
                if (modifier == null) continue;

                float multiplier = ToMultiplier(modifier);
                if (modifier.Stat == ModifiableStat.AttackDamage) damageMultiplier *= multiplier;
                else if (modifier.Stat == ModifiableStat.AttackSpeed) attackSpeedMultiplier *= multiplier;
            }

            // 배율 1(=강화 없음)인 축은 담지 않는다 — 원장에 죽은 항목을 남기지 않기 위해.
            if (!Mathf.Approximately(damageMultiplier, 1f))
            {
                dst.Add(new TowerStatModifier(TowerStat.AttackDamage, TowerModifierMode.Multiplier, damageMultiplier));
            }
            if (!Mathf.Approximately(attackSpeedMultiplier, 1f))
            {
                dst.Add(new TowerStatModifier(TowerStat.AttackSpeed, TowerModifierMode.Multiplier, attackSpeedMultiplier));
            }
        }
    }
}
