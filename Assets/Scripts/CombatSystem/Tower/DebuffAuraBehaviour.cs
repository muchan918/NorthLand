using UnityEngine;

namespace NorthLand.Combat
{
    // 반경 안의 적에게 DoT·슬로우를 지속 부여하는 오라. 상태의 소유는 대상의 StatusEffectHandler다 —
    // 대상이 반경을 벗어나 갱신이 끊겨도 남은 Duration만큼 효과가 흐르다 만료된다.
    //
    // 적이 계속 움직이므로 주기적 재스캔이 필수다. 예전에는 이 주기를 UniTask 루프 + CancellationTokenSource로
    // 직접 돌렸는데, 이제 호스트(Tower)가 페이즈 게이팅 후 Tick으로 구동하므로 취소 토큰 수명 관리가 사라졌다.
    [AddComponentMenu("")]   // 런타임 조립 전용
    [DisallowMultipleComponent]
    public sealed class DebuffAuraBehaviour : MonoBehaviour, ITowerBehaviour
    {
        Tower owner;
        TowerAsset.DebuffAuraFields aura;
        LayerMask targetLayerMask;
        int effectId;

        float tickTimer;
        readonly Collider[] hitBuffer = new Collider[32];

        public TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        // 오라 반경은 공격 사거리와 같은 원장 축을 쓴다 — "사거리"라는 개념을 둘로 두지 않기 위함.
        // 기본값만 다르다(SO의 MagicRadius). 타일 버프의 사거리 증가가 오라에도 그대로 반영된다.
        public float Radius =>
            aura == null ? 0f : owner.Stats.Evaluate(TowerStat.AttackRange, aura.Radius);

        // 0 이하 폭주 방지 하한.
        float Interval => Mathf.Max(aura != null ? aura.Interval : 0f, 0.05f);

        public void Initialize(in TowerBuildContext context)
        {
            owner = context.Owner;
            aura = context.Asset.Magic?.DebuffAura;
            targetLayerMask = context.EnemyLayerMask;

            // 소스키는 **인스턴스별**이다. 예전에는 TowerID 해시를 썼는데, 그러면 같은 종류 오라 타워
            // 여러 개가 대상의 StatusEffectHandler에서 한 슬롯을 공유해 서로를 갱신만 했다:
            //
            //   · 감속: MoveSpeedComposer가 소스별 곱산으로 합성하도록 만들어져 있는데(#233), 키가 뭉개져
            //     감속 타워 2개를 지어도 배율이 1중첩에 머물렀다. 이는 P1 돌진의 유일한 파훼 수단을
            //     무력화한다 — 파훼 불변식이 slow^n 을 요구한다(Docs/Monster/Boss/TankGraphSpec.md).
            //   · DoT: 같은 종류 타워를 더 지어도 틱 데미지가 늘지 않았다.
            //
            // 인스턴스별로 채번하면 버프 오라(#164)와 소스키 도메인이 대칭이 되고, 두 문제가 함께 해소된다.
            // ⚠ 밸런스 영향: 같은 종류 오라 타워를 겹쳐 지을 때 감속·DoT가 실제로 중첩된다.
            effectId = GetInstanceID();

            tickTimer = 0f;   // 밤 진입 직후 첫 Tick에서 즉시 1회 적용
        }

        public void Dispose()
        {
            // 부여한 효과를 회수하지 않는다 — 대상이 Duration을 스스로 소진하는 설계이므로,
            // 타워가 철거되면 남은 시간만큼 효과가 흐르다 만료되는 게 의도된 거동이다.
            tickTimer = 0f;
        }

        public void Tick(float deltaTime)
        {
            if (aura == null) return;

            tickTimer -= deltaTime;
            if (tickTimer > 0f) return;

            tickTimer = Interval;
            ApplyDebuff();
        }

        void ApplyDebuff()
        {
            OptionalDamage dot = aura.Damage;
            bool hasDot = dot != null && dot.HasDamage;

            float slowMultiplier = AuraModifiers.ComputeSlowMultiplier(aura.Modifiers);
            bool hasSlow = slowMultiplier < 1f;

            if (!hasDot && !hasSlow) return;   // 적용할 효과 없음

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, Radius, hitBuffer, targetLayerMask);

            for (int i = 0; i < count; i++)
            {
                IDamageable damageable = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (damageable == null || damageable.IsDead) continue;
                if (damageable.Faction == owner.Faction) continue;   // 디버프는 적군만

                if (damageable is not Component target) continue;

                if (!target.TryGetComponent(out StatusEffectHandler handler))
                {
                    handler = target.gameObject.AddComponent<StatusEffectHandler>();
                }

                if (hasDot) handler.ApplyOrRefresh(effectId, dot.DamageAmount, dot.TickInterval, aura.Duration, owner);
                if (hasSlow) handler.ApplySlow(effectId, slowMultiplier, aura.Duration);
            }
        }

        // 정보 패널에 이 오라가 기여할 줄. 반경 + 효과 요약(DoT / 슬로우).
        public string DescribeStats()
            => aura == null
                ? null
                : TowerStatsFormatter.Join(
                    TowerStatsFormatter.BuildRangeLine(Radius),
                    TowerStatsFormatter.BuildDotLine(aura.Damage),
                    TowerStatsFormatter.BuildSlowLine(AuraModifiers.ComputeSlowMultiplier(aura.Modifiers)));

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (aura == null) return;
            UnityEditor.Handles.color = new Color(0.6f, 0.2f, 0.85f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, Radius);
        }
#endif
    }
}
