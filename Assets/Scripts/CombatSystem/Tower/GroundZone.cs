using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 착탄 지점에 남아 반경 안의 적에게 효과를 재적용하는 지속 구역(#336).
    //
    // **로직은 `DebuffAuraAction.ApplyDebuff`와 같은 루프다** — `OverlapSphereNonAlloc` →
    // `HitEffect.Apply`. 다른 것은 두 가지뿐:
    //   ① 중심이 타워가 아니라 **착탄점에 고정**된다 (그래서 타워를 따라다니지 않는다)
    //   ② **수명이 있다** (오라는 타워가 서 있는 동안 계속)
    //
    // 그래서 신규 런타임 인프라가 0이다 — 효과의 소유·지속시간 소진·소스별 공존은 여전히 대상의
    // `StatusEffectHandler`가 맡고, 수치는 `TowerAsset.Effects`(`BurnStatus` 등)가 소유한다.
    // 이 클래스가 아는 것은 "어디에, 얼마나 오래, 얼마나 자주"뿐이다.
    //
    // ⚠ **소스 키를 장판 인스턴스별로 채번한다.** 타워 인스턴스로 채번하면(오라·명중 경로의 현행 규약)
    //   구역 2개가 겹쳐도 대상의 `Dictionary<int, DotState>` 슬롯 **하나**를 공유해 갱신만 되고
    //   중첩이 사라진다 — 2연발 타워의 "구역 두 개가 겹치면 두 배"가 성립하지 않는다.
    //   예외도 경고도 없이 조용히 틱이 반토막 나는 축이라 여기 명시한다.
    //
    // ⚠ **판정 반경과 시각 반경은 자동으로 맞지 않는다.** 반경은 SO(`GroundZoneFields.Radius`)가,
    //   보이는 크기는 이펙트 프리팹의 스케일이 정한다. 어긋나면 "불이 안 붙은 곳에서 타는" 그림이 되므로
    //   저작 시 기즈모(선택 시 표시)로 확인할 것.
    public class GroundZone : MonoBehaviour
    {
        // 지면에 깔린 구역이 위로 몇 유닛까지 판정하는지. 몬스터 부양 높이(WL-063: 경로 Y가 타일 상면
        // +6f, 그 위에 몸통)를 덮는 값이며 전기장(`SkillField.verticalRange`)과 같은 12f다.
        // ⚠ SO 저작 항목으로 빼지 않은 이유: 이 값은 타워의 성질이 아니라 **맵의 성질**이다 —
        // 타워마다 다르게 적을 이유가 없고, 저작 항목이 되면 값이 틀린 타워만 조용히 안 맞는다.
        // WL-063이 부양 높이의 단일 출처를 세우면 이 상수도 그쪽에서 파생시킬 자리다.
        const float VerticalRange = 12f;

        // 착탄점 아래 지면을 찾는 탐침. 맵 타일은 전부 `Tile` 레이어에 콜라이더를 갖는다(실측 390/390).
        const string k_GroundLayer = "Tile";
        const float k_GroundProbeUp = 4f;         // 착탄점보다 이만큼 위에서 아래로 쏜다
        const float k_GroundProbeDistance = 60f;  // 맵 높이 폭(0.8~6.2) + 부양·몸통을 넉넉히 덮는다

        static int groundMask;   // 이름 조회 1회 캐시(0 = 아직 미조회)

        Tower owner;
        IReadOnlyList<HitEffect> effects;
        LayerMask enemyMask;

        float radius;
        float remaining;    // 남은 수명(초)
        float interval;     // 재적용 주기(초)
        float tickTimer;

        Collider[] hitBuffer;

        /// 착탄 지점에 구역을 띄운다. 저작이 비었거나 소스 타워가 없으면 아무것도 하지 않는다.
        ///
        /// 이펙트 프리팹에 이 컴포넌트가 없어도 된다 — **런타임에 붙인다.** 그래서 벤더 파티클 팩의
        /// 프리팹을 그대로 지정할 수 있고(`Assets/Imported/` 무수정 규칙 유지), 아트가 이펙트를 교체할 때
        /// 프리팹에 스크립트를 다시 붙일 필요가 없다.
        public static GroundZone Spawn(
            TowerAsset.GroundZoneFields fields, Vector3 position, Tower owner, IReadOnlyList<HitEffect> effects)
        {
            if (fields == null || !fields.IsAuthored || owner == null) return null;

            // ⚠ 넘어온 착탄점은 **지면이 아니라 몬스터의 피격점**(`HitPosition`, 몸통 자식 트랜스폼)이다 —
            // 몬스터가 타일 상면 +6f에 떠 있고(WL-063) 거기에 프리팹별 몸통 높이가 더 얹힌다. 그대로 쓰면
            // 화염 구역이 적의 머리 높이에 뜬다. 수평은 착탄점 그대로 두고 높이만 지면으로 내린다.
            position.y = ResolveGroundY(position, owner);

            // ⚠ 회전은 **프리팹이 저작한 것을 그대로 쓴다** — `Quaternion.identity`로 덮으면 안 된다.
            // 파티클 팩 프리팹은 루트에 회전을 얹고 자식이 그걸 상쇄하는 형태로 저작돼 있는 것이 흔하다
            // (`AreaDamageFire`: 루트 X=270°, 불판 자식 3개가 X=+90°로 상쇄 → 월드 수평). 루트를 identity로
            // 덮으면 상쇄가 깨져 **불판이 90° 서 버린다**. 이 클래스가 벤더 프리팹을 무수정으로 받는 것을
            // 계약으로 삼는 이상(위 주석), 프리팹의 저작 상태가 곧 정답이다.
            GameObject obj = Instantiate(fields.ZonePrefab, position, fields.ZonePrefab.transform.rotation);

            GroundZone zone = obj.GetComponent<GroundZone>();
            if (zone == null) zone = obj.AddComponent<GroundZone>();

            zone.Initialize(fields, owner, effects);
            return zone;
        }

        /// 착탄점 **바로 아래**의 지면 높이. 구역이 실제로 깔릴 Y다.
        ///
        /// ⚠ **타워가 앉은 높이를 쓰면 안 된다.** 전투 맵은 평면이 아니다 — 실측(GameScene 390타일)에서
        /// 도로 앵커 Y = 0.80, 잔디 = 3.80(일부 5.00·6.20)으로 **길이 3만큼 파여 있다.** 타워는 잔디에 서고
        /// 몬스터는 도로를 걸으므로, 타워 Y를 그대로 쓰면 장판이 길바닥에서 3 떠올라 적의 허리에 걸린다.
        ///
        /// 몬스터 루트에서 부양 오프셋(6f)을 빼는 방법도 있지만 그 상수는 WL-063이 "단일 출처가 없다"고
        /// 열어 둔 매직 넘버라 여기까지 번지게 두지 않는다. 아래로 재면 **어느 타일 위든 맞고**, 나중에
        /// 높이차 지형이 들어와도 따라간다.
        static float ResolveGroundY(Vector3 position, Tower owner)
        {
            // 레이어 번호를 상수로 박으면 TagManager 순서가 바뀔 때 조용히 깨진다 — 이름으로 1회 조회한다.
            if (groundMask == 0) groundMask = 1 << LayerMask.NameToLayer(k_GroundLayer);

            // 착탄점보다 위에서 시작한다 — 착탄점이 이미 지면에 파고든 각도라도 타일 윗면을 잡는다.
            Vector3 from = position + Vector3.up * k_GroundProbeUp;

            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, k_GroundProbeDistance, groundMask))
                return hit.point.y;
            


            // 맵 밖 등으로 지면을 못 찾으면 타워 높이로 물러선다. 틀릴 수 있지만 착탄점(적의 머리 높이)을
            // 그대로 두는 것보다는 덜 틀리다 — 최소한 사람이 서 있을 만한 높이는 된다.
            return owner.transform.position.y;
        }

        void Initialize(TowerAsset.GroundZoneFields fields, Tower source, IReadOnlyList<HitEffect> hitEffects)
        {
            owner = source;
            effects = hitEffects;
            enemyMask = source.EnemyLayerMask;

            radius = fields.Radius;
            remaining = fields.Duration;
            interval = Mathf.Max(fields.Interval, 0.05f);   // 0 이하 폭주 방지 하한(오라와 같은 규칙)

            tickTimer = 0f;   // 생성 즉시 1회 적용 — 착탄과 동시에 불이 붙어야 한다

            hitBuffer ??= new Collider[32];
        }

        void Update()
        {
            // 소스 타워가 사라지면(철거·합성 소진·풀 반환) 구역도 사라진다. 파괴된 타워를 가리키는
            // `DamageInfo`를 계속 발행하면 처치 귀속(킬스택)·보상 축이 죽은 참조를 보게 된다.
            // 이미 걸린 화상은 대상이 소유하고 있어 남은 시간만큼 그대로 흐른다.
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }

            float deltaTime = Time.deltaTime;
            remaining -= deltaTime;
            tickTimer -= deltaTime;

            if (tickTimer <= 0f)
            {
                tickTimer = interval;
                Apply();
            }

            // 수명 판정을 적용 뒤에 두어 마지막 틱이 잘리지 않게 한다.
            if (remaining <= 0f) Destroy(gameObject);
        }

        void Apply()
        {
            if (effects == null || effects.Count == 0) return;

            // 수직 축 캡슐 — 수평 단면이 정확히 반경 `radius`의 원이라 지면에 깔린 이펙트와 1:1로 맞고,
            // 수직만 열어 부양한 적(WL-063)에게 닿는다. 구체로 두면 Spawn이 구역을 지면으로 내린 만큼
            // 위쪽 도달이 줄어 "불 위에 서 있는데 안 타는" 그림이 되고, 그렇다고 반경을 키우면 수평까지
            // 같이 커져 저작한 반경이 거짓이 된다. 전기장(#316 `SkillField.Tick`)이 같은 이유로 같은 형태다.
            int count = Physics.OverlapCapsuleNonAlloc(
                transform.position,
                transform.position + Vector3.up * VerticalRange,
                radius, hitBuffer, enemyMask);
            if (count == 0) return;

            // 소스 키의 baseId만 오라·명중 경로와 다르다(위 ⚠ 참조) — 채번 함수는 같은 것을 쓴다.
            int baseId = GetInstanceID();
            TowerStats stats = owner.Stats;

            for (int i = 0; i < count; i++)
            {
                IDamageable damageable = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (damageable == null || damageable.IsDead) continue;
                if (damageable.Faction == owner.Faction) continue;   // 적군만

                for (int e = 0; e < effects.Count; e++)
                {
                    HitEffect effect = effects[e];
                    if (effect == null) continue;   // [SerializeReference] rename 시 null 항목

                    // 합성 계승(#274 Phase 5) — 투사체·오라 경로와 같은 규칙.
                    if (!owner.IsEffectActive(effect.Kind)) continue;

                    effect.Apply(damageable, owner, stats, HitEffect.SourceKey(baseId, effect.Kind));
                }
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            // Apply의 캡슐 판정과 같은 모양 — 지면 원반만 그리면 수직 도달을 오해한다(`SkillField`와 같은 이유).
            UnityEditor.Handles.color = new Color(1f, 0.45f, 0.1f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, radius);
            UnityEditor.Handles.DrawWireDisc(transform.position + Vector3.up * VerticalRange, Vector3.up, radius);
        }
#endif
    }
}
