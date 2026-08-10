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

            GameObject obj = Instantiate(fields.ZonePrefab, position, Quaternion.identity);

            GroundZone zone = obj.GetComponent<GroundZone>();
            if (zone == null) zone = obj.AddComponent<GroundZone>();

            zone.Initialize(fields, owner, effects);
            return zone;
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

            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, hitBuffer, enemyMask);
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
            UnityEditor.Handles.color = new Color(1f, 0.45f, 0.1f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, radius);
        }
#endif
    }
}
