using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using NorthLand.Combat;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NorthLand.Combat
{
    // 마법 타워 공통 컴포넌트. TowerAsset(Magic)을 참조하고 사거리 내 대상에게 오라 효과를
    // UniTask 루프로 주기적(Interval)으로 갱신한다. MagicEffectType으로 Buff/Debuff 분기:
    //   Debuff → 적군 대상, DoT (StatusEffectHandler)         ← 구현됨
    //   Buff   → 아군 대상, 스탯 버프/힐                        ← 확장 지점(미구현)
    //
    // 효과의 지속시간·틱은 대상의 StatusEffectHandler가 소유하므로, 대상이 사거리를 벗어나면
    // 이 타워의 갱신만 끊길 뿐 남은 지속시간은 대상 쪽에서 계속 소진된다.
    // DamageInfo의 데미지 소스로 쓰이기 위해 IAttacker를 구현한다.
    public class AuraTower : MonoBehaviour, IAttacker
    {
        [SerializeField] TowerAsset data;

        // 디버프=적 레이어 / 버프=아군 레이어. 프리팹에서 지정.
        [SerializeField] LayerMask targetLayerMask;

        [Header("Debug")]
        [SerializeField] bool debugLog = false;

        readonly Collider[] hitBuffer = new Collider[32];
        CancellationTokenSource cts;
        int effectId;

        public Faction Faction => Faction.Player;

        bool IsMagic => data != null && data.TowerType == TowerType.Magic;
        bool IsDebuff => IsMagic && data.MagicEffectType == MagicEffectType.Debuff;
        bool IsBuff => IsMagic && data.MagicEffectType == MagicEffectType.Buff;

        // 현재 활성 오라 데이터 (null-safe). data.Magic 미할당 시에도 NRE 없이 null.
        TowerAsset.DebuffAuraFields DebuffAura => IsDebuff ? data.Magic?.DebuffAura : null;
        TowerAsset.BuffAuraFields BuffAura => IsBuff ? data.Magic?.BuffAura : null;

        // Buff/Debuff의 서로 다른 필드 클래스를 공통값으로 해석
        float Radius => DebuffAura?.Radius ?? BuffAura?.Radius ?? 0f;
        float Interval => DebuffAura?.Interval ?? BuffAura?.Interval ?? 0f;

        // IAttacker 계약(공개 스탯). 오라 타워라 값이 없으면 0 가드.
        public float AttackDamage => DebuffAura?.Damage != null ? DebuffAura.Damage.DamageAmount : 0f;
        public float AttackRange => Radius;
        public float AttackInterval => Interval;

        // 오라 타워는 단일 대상 즉시 공격 경로를 쓰지 않는다. 오라 루프로만 동작.
        public bool TryAttack(IDamageable target) => false;

        void OnEnable()
        {
            if (!IsMagic || data.Magic == null) return;   // 마법 타워가 아니거나 Magic 데이터 미할당이면 루프 미시작

            // 같은 TowerID끼리는 하나의 효과를 공유·갱신, 다른 종류는 별도.
            effectId = !string.IsNullOrEmpty(data.TowerID) ? data.TowerID.GetHashCode() : GetInstanceID();

            cts = new CancellationTokenSource();
            AuraLoop(cts.Token).Forget();
        }

        void OnDisable()
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = null;
        }

        async UniTaskVoid AuraLoop(CancellationToken ct)
        {
            float loopInterval = Mathf.Max(Interval, 0.05f);   // 0 이하 폭주 방지

            while (!ct.IsCancellationRequested)
            {
                Tick();

                bool canceled = await UniTask
                    .Delay(TimeSpan.FromSeconds(loopInterval), cancellationToken: ct)
                    .SuppressCancellationThrow();
                if (canceled) return;
            }
        }

        void Tick()
        {
            // 낮/밤 게이팅: Tower.cs와 동일하게 밤 페이즈에만 동작 (WL-019, 두 타워 규칙 일치)
            if (DayNightManager.Instance != null &&
                DayNightManager.Instance.CurrentPhase != DayNightManager.Phase.Night) return;

            var aura = DebuffAura;
            if (aura != null) ApplyDebuff(aura);
            // else if (BuffAura != null) ApplyBuff(BuffAura);   // TODO(확장): 버프 미구현
        }

        // 사거리 내 적군에게 DoT 디버프를 갱신. 상태 소유는 대상의 StatusEffectHandler.
        void ApplyDebuff(TowerAsset.DebuffAuraFields aura)
        {
            var dot = aura?.Damage;
            if (dot == null || !dot.HasDamage) return;   // 현재는 DoT만 처리 (Modifiers 확장은 추후)

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, aura.Radius, hitBuffer, targetLayerMask);

            for (int i = 0; i < count; i++)
            {
                var dmg = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (dmg == null || dmg.IsDead) continue;
                if (dmg.Faction == Faction) continue;    // 디버프는 아군 제외 (적군만)

                if (dmg is not Component target) continue;

                var handler = target.GetComponent<StatusEffectHandler>();
                if (handler == null) handler = target.gameObject.AddComponent<StatusEffectHandler>();

                handler.debugLog = debugLog;
                handler.ApplyOrRefresh(effectId, dot.DamageAmount, dot.TickInterval, aura.Duration, this);
            }
        }

        // TODO(확장): 버프 오라 — 아군(Faction == Faction) 대상, 스탯 버프/힐 적용.
        //             스탯 modifier 런타임 시스템이 준비되면 여기서 BuffAura(Modifiers/Damage)를 소비한다.
        // void ApplyBuff(TowerAsset.BuffAuraFields aura) { ... }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!IsMagic) return;
            Handles.color = IsBuff ? new Color(0.3f, 0.7f, 1f) : new Color(0.6f, 0.2f, 0.85f);
            Handles.DrawWireDisc(transform.position, Vector3.up, Radius);
        }
#endif
    }
}
