using System.Collections.Generic;

namespace NorthLand.Combat
{
    // 타워 스탯 4축. CombatSpace.TileBuffStat과 1:1 대응하며, 그 변환은 Tower.ApplyTileBuff 한 곳에만 둔다.
    //
    // AuraRadius를 AttackRange와 **나눠 둔 이유**: 사거리 축을 공유하던 동안 사거리 버프 타일의 flat
    // 증가분이 오라 반경에 그대로 실려, 기본 반경이 작은 오라 타워가 훨씬 큰 이득을 봤다
    // (flame_field 9.6 → 30.6, 지름 3.2배 = 화면 면적 10배). 축이 갈려야 타일이
    // "공격 사거리 +n칸 / 오라 반경 +m칸"을 따로 약속할 수 있다.
    public enum TowerStat
    {
        AttackDamage,
        AttackRange,
        AttackSpeed,

        // 새 항목은 **뒤에만** 추가한다 — SO에 int로 직렬화돼 있어 중간 삽입은 기존 저작을 조용히 밀어낸다.
        AuraRadius,
    }

    // 합성 방식.
    //   Flat       — 기본값에 더한다(사거리 +3).
    //   Percentage — 백분율 가산(10 = +10%). 타일 버프 규약과 같은 의미.
    //   Multiplier — 배율로 받는다(1.25 = +25%). 기존 Tower.ApplyBuff 규약을 그대로 수용하기 위한 축이며,
    //                내부에는 보너스(0.25)로 저장돼 소스별로 **합산** 중첩된다.
    public enum TowerModifierMode
    {
        Flat,
        Percentage,
        Multiplier,
    }

    public readonly struct TowerStatModifier
    {
        public readonly TowerStat Stat;
        public readonly TowerModifierMode Mode;
        public readonly float Value;

        public TowerStatModifier(TowerStat stat, TowerModifierMode mode, float value)
        {
            Stat = stat;
            Mode = mode;
            Value = value;
        }
    }

    // 타워 스탯 modifier의 단일 원장(WL-050/WL-081 해소). 타일 버프·버프 스킬·버프 오라·보스 마력 봉인이
    // 전부 이 하나로 수렴하며, 합성 규칙은 Evaluate 한 곳에만 산다:
    //
    //     (기본값 + Σflat) × (1 + Σpercent/100) × (1 + Σ배율보너스)
    //
    // 소스(sourceId)당 modifier 묶음 하나를 보유한다. 같은 소스로 다시 Apply하면 **교체**되고(refresh),
    // 서로 다른 소스는 합산 중첩된다 — 기존 Tower.activeBuffs의 계약을 그대로 옮긴 것이다.
    // 소스키 도메인(어떤 값을 쓰는지)은 호출부 규약이며 Tower 주석에 정리돼 있다.
    //
    // MonoBehaviour가 아닌 순수 C#이고 Time.time을 직접 읽지 않는다(now를 주입받는다) —
    // 씬 없이 EditMode 테스트로 합성 규칙을 검증할 수 있게 하기 위함이다.
    public sealed class TowerStats
    {
        // 축 수는 enum에서 파생시킨다 — 손으로 맞추면 축을 추가한 사람이 여기를 잊는 순간
        // Evaluate가 IndexOutOfRange로 죽는다(원인이 증상에서 멀다).
        static readonly int StatCount = System.Enum.GetValues(typeof(TowerStat)).Length;

        sealed class Source
        {
            public readonly List<TowerStatModifier> Modifiers = new List<TowerStatModifier>();
            public float Expiry;
        }

        readonly Dictionary<int, Source> sources = new Dictionary<int, Source>();
        readonly List<int> expiredScratch = new List<int>();
        readonly Stack<Source> pool = new Stack<Source>();          // 소스 재사용(Apply마다 힙 할당 방지)
        readonly List<TowerStatModifier> singleScratch = new List<TowerStatModifier>(1);

        // 집계 캐시. 변경 시에만 재계산하고 조회는 배열 인덱싱만 한다.
        readonly float[] flat = new float[StatCount];
        readonly float[] percent = new float[StatCount];
        readonly float[] multiplierBonus = new float[StatCount];

        public bool HasAny => sources.Count > 0;

        /// <summary>
        /// 원장이 바뀔 때마다 오르는 카운터(#536). 표시부가 "다시 읽어야 하는가"를 이 값 하나로 판단한다 —
        /// 정보 패널이 매 프레임 행을 재조립하면 문자열 할당이 프레임마다 쌓이는데, 실제로 값이 바뀌는
        /// 순간은 드물기 때문이다(램프 스택 증감·버프 적용·만료).
        ///
        /// <para><b>증가 지점은 <see cref="Recompute"/> 하나다.</b> Apply·Remove·Prune·Clear가 전부 그것을
        /// 거치므로 새 변경 경로가 생겨도 자동으로 포함된다 — 각 진입점에서 따로 올리면 하나를 흘린다.</para>
        /// </summary>
        public int Version { get; private set; }

        /// 기본값에 이 원장의 modifier를 합성한 최종 값.
        /// 배율형 스탯(공격속도)은 baseValue에 1f를 넘겨 순수 배율로 받는다.
        ///
        /// **0 하한이 필수다.** 배율 모드는 보너스를 합산하므로(1.0 → +0, 0.5 → −0.5) 디버프 방향 소스가
        /// 겹치면 합이 −1 아래로 내려가 결과가 음수가 된다. 예: 보스 P3 마력 봉인은 sourceId가 에이전트별이라
        /// 보스 2기가 각각 damageMul 0.5를 걸면 보너스 합 −1.0(=데미지 0), 3기면 음수다.
        /// 하류에 클램프가 없어(`AttackAction` → `Projectile` → `Enemy.currentHp -= amount`) 음수 데미지가
        /// 그대로 **회복**이 된다. 원장이 단일 출처이므로 여기 한 줄이 전 소비처를 덮는다.
        public float Evaluate(TowerStat stat, float baseValue)
        {
            int i = (int)stat;
            float result = (baseValue + flat[i]) * (1f + percent[i] / 100f) * (1f + multiplierBonus[i]);

            return result > 0f ? result : 0f;
        }

        /// 한 소스의 modifier 묶음을 등록·갱신한다(교체 semantics).
        /// duration이 0 이하면 지속형(만료 없음 → Remove로만 해제), 0보다 크면 now+duration에 만료된다.
        /// modifiers가 비어 있으면 그 소스를 제거한다 — "아무 효과 없는 소스"를 원장에 남기지 않는다.
        public void Apply(int sourceId, IReadOnlyList<TowerStatModifier> modifiers, float duration, float now)
        {
            if (modifiers == null || modifiers.Count == 0)
            {
                Remove(sourceId);
                return;
            }

            if (!sources.TryGetValue(sourceId, out Source source))
            {
                source = pool.Count > 0 ? pool.Pop() : new Source();
                sources[sourceId] = source;
            }

            source.Modifiers.Clear();
            for (int i = 0; i < modifiers.Count; i++)
            {
                source.Modifiers.Add(modifiers[i]);
            }
            source.Expiry = duration > 0f ? now + duration : float.PositiveInfinity;

            Recompute();
        }

        /// 단일 modifier 편의 오버로드. 묶음 버전과 동일하게 그 소스를 **교체**한다.
        public void Apply(int sourceId, TowerStatModifier modifier, float duration, float now)
        {
            singleScratch.Clear();
            singleScratch.Add(modifier);
            Apply(sourceId, singleScratch, duration, now);
        }

        /// 특정 소스의 modifier를 즉시 제거(만료 대기 없이). 없으면 false.
        public bool Remove(int sourceId)
        {
            if (!Detach(sourceId)) return false;

            Recompute();
            return true;
        }

        /// 만료된 소스를 정리한다. 실제로 제거된 게 있으면 true.
        public bool Prune(float now)
        {
            if (sources.Count == 0) return false;

            expiredScratch.Clear();
            foreach (KeyValuePair<int, Source> pair in sources)
            {
                if (now >= pair.Value.Expiry) expiredScratch.Add(pair.Key);
            }

            if (expiredScratch.Count == 0) return false;

            for (int i = 0; i < expiredScratch.Count; i++)
            {
                Detach(expiredScratch[i]);
            }

            Recompute();
            return true;
        }

        /// 전량 해제. 풀링 등으로 인스턴스가 재사용될 때 이전 생명주기의 버프가 고착되지 않도록 쓴다.
        public void Clear()
        {
            if (sources.Count == 0) return;

            expiredScratch.Clear();
            foreach (KeyValuePair<int, Source> pair in sources)
            {
                expiredScratch.Add(pair.Key);
            }
            for (int i = 0; i < expiredScratch.Count; i++)
            {
                Detach(expiredScratch[i]);
            }

            Recompute();
        }

        // 집계 재계산 없이 소스만 떼어낸다(여러 건을 지운 뒤 한 번만 Recompute하기 위해 분리).
        bool Detach(int sourceId)
        {
            if (!sources.TryGetValue(sourceId, out Source source)) return false;

            sources.Remove(sourceId);
            source.Modifiers.Clear();
            pool.Push(source);
            return true;
        }

        void Recompute()
        {
            Version++;

            for (int i = 0; i < StatCount; i++)
            {
                flat[i] = 0f;
                percent[i] = 0f;
                multiplierBonus[i] = 0f;
            }

            foreach (KeyValuePair<int, Source> pair in sources)
            {
                List<TowerStatModifier> modifiers = pair.Value.Modifiers;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    TowerStatModifier modifier = modifiers[i];
                    int stat = (int)modifier.Stat;

                    switch (modifier.Mode)
                    {
                        case TowerModifierMode.Flat:
                            flat[stat] += modifier.Value;
                            break;
                        case TowerModifierMode.Percentage:
                            percent[stat] += modifier.Value;
                            break;
                        case TowerModifierMode.Multiplier:
                            // 배율을 보너스로 환산해 더한다 — 실효 배율 = 1 + Σ보너스(합산 중첩).
                            multiplierBonus[stat] += modifier.Value - 1f;
                            break;
                    }
                }
            }
        }
    }
}
