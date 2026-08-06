using System;
using UnityEngine;

namespace NorthLand.Combat
{
    // 전투 실적으로 이 타워가 스스로 강해지는 액션(#300).
    //
    // ★ **새 스탯 경로를 파지 않는다.** `AttackAction.Damage`/`Interval`,
    // `BeamAction.DamagePerTick`/`TickInterval`, `DamageOverTimeEffect`의 DoT 수치가 이미 전부
    // `Owner.Stats.Evaluate`를 통과하므로, 스택을 **원장에 소스 하나로 얹기만 하면** 공격·빔·DoT·
    // 정보 패널·선택 사거리 원이 전부 따라온다. 그래서 이 액션은 기존 액션을 한 줄도 고치지 않는다.
    //
    // ⚠ 이 램프는 **타워 전역**이다. "한 대상을 오래 조준할수록 아프다"(단일 인페르노)처럼 대상이
    // 바뀌면 리셋돼야 하는 축은 원장으로 표현할 수 없다 — 그쪽은 `BeamFields.LockRamp`가 대상별로
    // 따로 계산한다. 두 축은 수치 규약(`RampProfile`)만 공유한다.
    [Serializable]
    public sealed class RampAction : TowerAction
    {
        // ── 런타임 상태 (직렬화 금지 — TowerAction 규칙 ③) ──────────────────
        [NonSerialized] TowerAsset.RampFields fields;
        [NonSerialized] int stacks;
        [NonSerialized] float decayTimer;

        // 구독 상태. Initialize는 여러 번 들어올 수 있어(재활성화·같은 SO 재조립) 중복 구독을 막아야 한다.
        [NonSerialized] bool subscribed;
        [NonSerialized] RampTrigger subscribedTrigger;

        // 마지막으로 조립된 SO. **정체성이 바뀌었는지**를 판정하는 근거다(아래 OnInitialize 주석).
        [NonSerialized] TowerAsset boundAsset;

        // ⚠ **`Always`여야 한다.** `NightOnly`면 낮에 Tick이 돌지 않아 감쇠 타이머가 멈추고,
        // 전날 밤에 쌓은 최고 스택이 다음 밤 시작까지 그대로 남는다. `Always`로 두면 낮 동안
        // 자연히 비워지므로 **밤 종료 훅이 아예 필요 없다** — 액션이 DayNightManager를 직접 폴링하는
        // 것은 금지돼 있고(WL-044), 페이즈 전환 통지를 위해 호스트에 훅을 새로 뚫을 이유도 없어진다.
        public override TowerActivePhase ActivePhase => TowerActivePhase.Always;

        // 닿는 거리 개념이 없다 — 사거리 원은 공격·오라 액션이 그린다.
        public override float DisplayRange => 0f;

        /// 현재 스택. 정보 패널·디버그용.
        public int Stacks => stacks;

        /// 현재 실효 배율(스택 0이면 1).
        public float Multiplier => Authored ? fields.Profile.Multiplier(stacks) : 1f;

        bool Authored => fields?.Profile != null && fields.Profile.IsAuthored;

        protected override void OnInitialize(TowerAsset asset)
        {
            fields = asset.Ramp;

            // ⚠ **같은 SO면 스택을 유지한다.** 합성 커맨드의 Release/Reoccupy가 OnDisable/OnEnable
            // 왕복을 쓰므로(Tower.cs의 Build 주석) 여기서 0으로 밀면 롤백된 타워의 성장이 증발한다.
            // 반대로 **다른 SO로 재조립**되면 정체성이 바뀐 것이라 이전 성장분은 의미를 잃는다 —
            // Tower.Build가 같은 자리에서 `activeKinds`를 null로 되돌리는 것과 같은 판단이다.
            if (boundAsset != asset)
            {
                boundAsset = asset;
                stacks = 0;
            }

            decayTimer = fields?.Profile != null ? fields.Profile.DecaySeconds : 0f;

            Resubscribe();

            // OnDisable이 원장을 통째로 비웠어도(Tower.OnDisable → stats.Clear) 가진 스택을 되돌려
            // 놓는다. 타일 버프가 같은 이유로 OnEnable에서 재푸시되는 것과 같은 축이다(Tower.cs).
            PushStacks();
        }

        public override void Dispose()
        {
            // static 이벤트라 죽은 구독자가 남으면 파괴된 타워를 계속 건드린다(BurnBuff와 같은 규율).
            Unsubscribe();

            // 원장에 남긴 소스를 걷어낸다. OnDisable 경로에서는 직후 stats.Clear가 어차피 비우지만,
            // **같은 SO 재조립 경로**(Build → Dispose → Initialize)에는 Clear가 없어 여기가 유일한 회수 지점이다.
            Owner?.Stats.Remove(SourceId);

            // ⚠ stacks는 지우지 않는다 — 위 OnInitialize 주석의 합성 롤백 때문이다.
        }

        /// 웨이브가 끝나면 성장이 전부 풀린다 — **감쇠 설정과 무관한 일괄 규칙**이다.
        ///
        /// 그래서 `DecaySeconds = 0`은 "영구"가 아니라 **"이 웨이브 동안 유지"**를 뜻한다. 이 경계를 두는
        /// 이유는 성장이 Run 내내 누적되면 ① 웨이브가 갈수록 눈덩이가 되어 밸런싱이 후반 한 점에 묶이고
        /// ② 이어하기(#270)가 스택을 저장하지 않으므로 "저장 후 재개하면 약해지는" 불일치가 생긴다는 것.
        /// 웨이브 안으로 가두면 두 문제가 함께 사라진다.
        ///
        /// 웨이브별로 다르게 굴리고 싶어지면 `RampProfile`에 유지 플래그를 하나 두면 되지만, 지금은
        /// 규칙이 하나인 편이 플레이어가 예측하기 쉽다.
        public override void OnWaveEnd()
        {
            if (stacks == 0) return;

            stacks = 0;
            PushStacks();   // 스택 0 → 원장에서 소스를 제거한다
        }

        public override void Tick(float deltaTime)
        {
            if (!Authored || stacks <= 0) return;

            float decay = fields.Profile.DecaySeconds;
            if (decay <= 0f) return;   // 0 = 감쇠 없는 영구 성장(킬스택 타워)

            decayTimer -= deltaTime;
            if (decayTimer > 0f) return;

            // 한 번에 전부 잃지 않고 1씩 식는다 — 상한 근처에서 잠깐 놓쳤다고 성장이 0으로 무너지면
            // "계속 때려서 유지한다"는 조작 목표가 성립하지 않는다.
            decayTimer = decay;
            stacks--;
            PushStacks();
        }

        public override string DescribeStats()
        {
            if (!Authored) return null;

            return TowerStatsFormatter.BuildRampLine(
                fields.Stat, stacks, fields.Profile.MaxStacks, fields.Profile.Multiplier(stacks));
        }

        // ── 스택 ───────────────────────────────────────────────────────────
        void AddStack()
        {
            // 상한에 있어도 타이머는 되감는다 — 계속 싸우는 동안 식지 않는다.
            decayTimer = fields.Profile.DecaySeconds;

            if (stacks >= fields.Profile.MaxStacks) return;

            stacks++;
            PushStacks();
        }

        // 원장은 **소스 단위 교체** semantics라(TowerStats.Apply) 스택이 바뀔 때마다 현재 총량을
        // 그대로 다시 밀면 된다 — 증분을 누적 관리할 필요가 없다.
        void PushStacks()
        {
            if (Owner == null) return;

            if (!Authored || stacks <= 0)
            {
                Owner.Stats.Remove(SourceId);   // 효과 없는 소스를 원장에 남기지 않는다
                return;
            }

            Owner.Stats.Apply(
                SourceId,
                new TowerStatModifier(fields.Stat, TowerModifierMode.Multiplier, fields.Profile.Multiplier(stacks)),
                0f,           // 지속형 — 만료는 이 액션의 감쇠가 관리한다
                Time.time);
        }

        // ── 트리거 구독 ─────────────────────────────────────────────────────
        void Resubscribe()
        {
            // 미저작이면 아예 구독하지 않는다 — 이벤트를 받아도 배율이 1이라 할 일이 없고,
            // "무동작이면 비용도 0"이라는 규칙을 따른다(AttackAction.canFire와 같은 축).
            if (!Authored)
            {
                Unsubscribe();
                return;
            }

            if (subscribed && subscribedTrigger == fields.Trigger) return;

            Unsubscribe();

            switch (fields.Trigger)
            {
                case RampTrigger.Kill:
                    Enemy.Killed += HandleKill;
                    break;
                default:
                    Projectile.DamageDealt += HandleHit;
                    break;
            }

            subscribed = true;
            subscribedTrigger = fields.Trigger;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;

            // 구독 당시의 트리거로 해제한다 — 그 사이에 fields.Trigger가 바뀌었을 수 있다.
            switch (subscribedTrigger)
            {
                case RampTrigger.Kill:
                    Enemy.Killed -= HandleKill;
                    break;
                default:
                    Projectile.DamageDealt -= HandleHit;
                    break;
            }

            subscribed = false;
        }

        // 두 핸들러 모두 **자기 타워가 낸 결과만** 센다. static 이벤트라 씬의 모든 램프 타워가 같은
        // 통지를 받으므로 이 필터가 곧 귀속 규칙이다.
        void HandleHit(IAttacker source, IDamageable victim)
        {
            if (!ReferenceEquals(source, Owner)) return;
            AddStack();
        }

        void HandleKill(IAttacker source, Enemy victim)
        {
            if (!ReferenceEquals(source, Owner)) return;
            AddStack();
        }
    }
}
