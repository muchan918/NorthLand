using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 반경 안의 적에게 효과를 지속 부여하는 오라. 상태의 소유는 대상의 StatusEffectHandler다 —
    // 대상이 반경을 벗어나 갱신이 끊겨도 남은 Duration만큼 효과가 흐르다 만료된다.
    //
    // 적이 계속 움직이므로 주기적 재스캔이 필수다(버프 오라와 달리 이벤트로 접을 수 없다).
    //
    // **거는 효과는 공격 액션과 같은 `TowerAsset.Effects`를 쓴다**(#274 Phase 4). "맞으면 화상"과
    // "장판에 화상"은 거는 방식만 다르고 효과 자체는 같기 때문이다 — 덕분에 화상 장판 타워가
    // 새 코드 없이 만들어진다. 예전에는 `DebuffAuraFields`에 DoT·감속이 수기 필드로 따로 박혀 있었다.
    [Serializable]
    public sealed class DebuffAuraAction : TowerAction
    {
        [NonSerialized] TowerAsset.DebuffAuraFields aura;
        [NonSerialized] List<HitEffect> effects;
        [NonSerialized] LayerMask targetLayerMask;
        [NonSerialized] float tickTimer;
        [NonSerialized] Collider[] hitBuffer;

        public override TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        // 오라 반경은 **자기 축**(TowerStat.AuraRadius)을 쓴다. 예전에는 공격 사거리 축을 공유해
        // "사거리 개념을 둘로 두지 않는다"를 만족했는데, 버프 타일이 증가량을 칸 단위 절댓값으로
        // 약속하는 순간 그 전제가 깨졌다 — 오라 기본 반경(1.6~2.7칸)이 공격 사거리(3칸)보다 작아
        // 같은 "+3칸"이 오라에서는 지름 3배가 됐다(장판이 상시 보이니 그대로 드러난다).
        //
        // 축만 갈렸고 단일 출처는 유지된다 — 판정(ApplyDebuff)·선택 원·장판(AuraZoneVisual)이
        // 전부 이 프로퍼티 하나를 본다.
        public float Radius =>
            aura == null ? 0f : Owner.Stats.Evaluate(TowerStat.AuraRadius, aura.Radius);

        // 선택 사거리 원은 오라 반경을 그린다 — 배치 프리뷰 원(TowerPlacer가 PreviewRadius로 그림)과
        // 같은 값이라 "놓을 때 보이던 원이 선택하면 사라진다"는 비일관이 생기지 않는다.
        public override float DisplayRange => Radius;

        // 재스캔 주기. 0 이하 폭주 방지 하한.
        //
        // 이 값은 원장을 거치지 않는다(공속 modifier에 반응하지 않음). 재스캔을 빠르게 해도 DoT는 이미
        // 대상이 소유하고 있어 갱신만 더 자주 될 뿐 피해가 늘지 않기 때문이다 — 독 타워에서 "공속"의
        // 의미를 갖는 축은 효과 쪽 TickInterval이고, 그건 HitEffect가 원장을 거쳐 합성한다.
        float Interval => Mathf.Max(aura != null ? aura.Interval : 0f, 0.05f);

        // 판정 캡슐의 수직 반길이. **사본을 만들지 않고 `GroundZone.VerticalRange`를 그대로 참조한다** —
        // 둘 다 바닥에 깔린 장판이고, 몬스터가 경로 Y에서 부양(WL-063)한 채 그 위에 몸통을 얹는
        // 같은 맵 성질을 상대하므로 값이 갈리면 한쪽만 조용히 어긋난다. 그 상수의 주석에 "왜 SO 저작
        // 항목이 아닌가"(맵의 성질이지 타워의 성질이 아니다)까지 함께 있다.

        protected override void OnInitialize(TowerAsset asset)
        {
            aura = asset.DebuffAura;
            effects = asset.Effects;
            targetLayerMask = Owner.EnemyLayerMask;
            hitBuffer ??= new Collider[32];

            tickTimer = 0f;   // 밤 진입 직후 첫 Tick에서 즉시 1회 적용
        }

        public override void Dispose()
        {
            // 부여한 효과를 회수하지 않는다 — 대상이 Duration을 스스로 소진하는 설계이므로,
            // 타워가 철거되면 남은 시간만큼 효과가 흐르다 만료되는 게 의도된 거동이다.
            tickTimer = 0f;
        }

        public override void Tick(float deltaTime)
        {
            if (aura == null || effects == null || effects.Count == 0) return;

            tickTimer -= deltaTime;
            if (tickTimer > 0f) return;

            tickTimer = Interval;
            ApplyDebuff();
        }

        void ApplyDebuff()
        {
            Vector3 origin = Origin.position;
            float radius = Radius;                               // 원장(버프 타일) 평가는 틱당 1회면 된다

            // **구체가 아니라 수직 축 캡슐**이다. 수평 단면이 정확히 반경 `radius`인 원이라 선택 사거리
            // 원·장판 이펙트와 수평으로 1:1이고, 수직만 열어 부양한 적(WL-063)에게 닿는다 —
            // `GroundZone.Apply`와 같은 형태이고 같은 이유다. 구체는 타워 원점(타일 윗면)과 적 몸통의
            // 높이차만큼 수평 도달이 줄어, 같은 사거리인데 **적 종류마다** 닿는 거리가 달랐다
            // (실측 R=12·부양 6f에서 Flying_Bat 10.1 / Blue_Grummy 12.1 — 원은 12).
            //
            // **위아래 대칭으로 연다**(`SkillHitScan.CollectEnemies`와 같은 형태). 원점이 지면이 아니라
            // 타워 루트(=타일 윗면)라, 위로만 열면 원점보다 **낮은** 곳의 수평 도달이 아래쪽 반구만큼
            // 다시 줄어든다 — 실제로 몬스터 경로가 타워 원점보다 낮다(실측: 타워 4.50 / 경로 1.70).
            // 지금 배치에서는 몸통이 위로 뻗어 차이가 0이지만(실측 ΔY −2.8에서 ±0.00), 잔디·도로
            // 타일 높이차가 커지면 되살아난다(ΔY −8에서 Flying_Bat 12.25 → 13.35). 타워 아래에는 적이
            // 없으므로 아래로 여는 대가는 없다 — 한 항 추가로 타일 높이 의존이 사라진다.
            //
            // 판정 시점은 **적 콜라이더가 이 원에 닿는 순간**이다(피벗이 들어오는 순간이 아니라).
            // "몸이 장판에 들어가면 묻는다"가 기획 의도라 표면 판정을 의도적으로 남긴다(#541).
            // 그 대가로 실효 도달은 `radius + 적 콜라이더 반경`(월드 1.35~5)이라 표기 반경보다 넓고
            // **적 크기에 비례**한다 — 밸런싱에서 표기 반경을 그대로 도달 거리로 읽지 말 것.
            // 몬스터 아트는 콜라이더보다 크므로(전 종 +0.6~2.3) 발동 시점엔 그림이 이미 원 안이다.
            // ⚠ 버프 오라(`BuffAuraAction.CollectTargets`)는 대상 **피벗 거리**를 쓴다 — 두 오라의 판정
            // 모양이 다르다는 뜻이므로, 한쪽 규칙을 바꿀 때 다른 쪽도 같이 볼 것.
            Vector3 vertical = Vector3.up * GroundZone.VerticalRange;
            int count = Physics.OverlapCapsuleNonAlloc(
                origin - vertical, origin + vertical,
                radius, hitBuffer, targetLayerMask);
            if (count == 0) return;

#if UNITY_EDITOR
            // 포화 = 초과분이 **말없이 버려진** 상태. `Physics.Overlap*NonAlloc`은 버퍼가 차면 나머지를
            // 그냥 반환하지 않으므로, 증상이 "밀집 웨이브에서만 가끔 안 걸리는 적이 있다"로 나타나
            // 재현이 안 된다(`GroundZone.Apply`와 같은 처방·같은 이유). 이 자리는 특히 값이 커졌다 —
            // 판정이 구체에서 원기둥으로 넓어졌고 재스캔이 초당 1회에서 10회가 됐으며, 반경이 가장 큰
            // `choco_tower`(16)의 감속 누락은 보스 P1 파훼 수단에 닿는다.
            // 버퍼 크기 산정 근거 합의와 `SkillHitScan.CollectEnemiesInCapsule`(포화 시 성장 + 다중
            // 콜라이더 중복 제거)의 공통 헬퍼화는 WL-170 본안이고, 여기서는 드러내는 것까지만 한다.
            if (count == hitBuffer.Length)
                Debug.LogWarning($"[DebuffAuraAction] 판정 버퍼 포화({count}) — 초과분이 누락됩니다. " +
                                 $"반경={radius:0.#}", Owner);
#endif

            // 소스 키는 HitEffect.SourceKey — 투사체 경로(Projectile.ApplyEffects)와 **같은 함수**라,
            // "맞아서 걸린 화상"과 "장판에서 걸린 화상"이 같은 슬롯을 쓴다.
            int baseId = Owner.GetInstanceID();
            TowerStats stats = Owner.Stats;

            for (int i = 0; i < count; i++)
            {
                IDamageable damageable = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (damageable == null || damageable.IsDead) continue;
                if (damageable.Faction == Owner.Faction) continue;   // 디버프는 적군만

                for (int e = 0; e < effects.Count; e++)
                {
                    HitEffect effect = effects[e];
                    if (effect == null) continue;

                    // 합성 계승(#274 Phase 5) — 투사체 경로(Projectile.ApplyEffects)와 같은 규칙이다.
                    if (!Owner.IsEffectActive(effect.Kind)) continue;

                    effect.Apply(damageable, Owner, stats, HitEffect.SourceKey(baseId, effect.Kind));
                }
            }
        }

        // 반경(원장 축 AuraRadius — 버프 타일이 바꾼다) + 효과 요약(#536).
        // 효과 수치는 **실효값**(원장 합성 후)이라 표기와 실제가 일치한다.
        public override void DescribeStatRows(List<TowerStatRowData> into)
        {
            if (aura == null) return;

            into.Add(TowerStatRowData.Stat(TowerStatsFormatter.k_AuraRadiusKey, aura.Radius, Radius));

            AttackAction.DescribeEffectRows(effects, Owner, into);
        }

#if UNITY_EDITOR
        public override void DrawGizmos()
        {
            if (aura == null) return;
            UnityEditor.Handles.color = new Color(0.6f, 0.2f, 0.85f);
            UnityEditor.Handles.DrawWireDisc(Origin.position, Vector3.up, Radius);
        }
#endif
    }
}
