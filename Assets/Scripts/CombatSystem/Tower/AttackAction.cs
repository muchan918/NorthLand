using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 투사체를 쏘는 액션. Single/Area/Chain은 별개 액션이 아니라 **명중 방식(ProjectileImpact)만**
    // 다르다 — 대상 탐색·쿨다운·발사 경로가 완전히 같아서, 이미 분리돼 있는 ProjectileImpact를 전략으로 쓴다.
    // 마찬가지로 유도/포격은 ProjectileFlight가 갈라낸다. 둘 다 SO가 정한다(#274).
    [Serializable]
    public sealed class AttackAction : TowerAction
    {
        // ── 런타임 상태 (직렬화 금지 — TowerAction 규칙 ③) ──────────────────
        [NonSerialized] TowerAsset.AttackFields fields;
        [NonSerialized] ProjectileFlight flight;
        [NonSerialized] ProjectileImpact impact;
        [NonSerialized] List<HitEffect> effects;   // SO의 Effects — 투사체에 실어 보낸다

        // 대상 탐색용 마스크를 impact와 별도로 보관한다 — ProjectileImpact.MakeSingle()은 EnemyMask를
        // 채우지 않으므로(스플래시·체인만 마스크가 필요) impact에서 되읽으면 단일 타워가 아무도 못 찾는다.
        [NonSerialized] LayerMask enemyLayerMask;

        [NonSerialized] float cooldownTimer;

        // 착탄 지점에 남길 구역(#336). 미저작이면 null 취급이라 기존 타워는 이 축을 타지 않는다.
        [NonSerialized] TowerAsset.GroundZoneFields zone;

        // 이번 공격 사이클에서 아직 남은 연발 수(#336). 0 = 사이클 종료(다음 발이 새 사이클의 첫 발).
        [NonSerialized] int burstRemaining;

        // 이번 연발 사이클이 붙들고 있는 대상(#387). 사이클 밖에서는 null이다.
        //
        // ★ 대상 고정의 **유일한 소유자**다. 호스트(`Tower.AcquireTarget`)는 "지금 정책 1위가 누구인가"만
        //   답하고 아무것도 붙들지 않는다 — 붙들면 조준 정책이 재선정 순간에만 의미를 갖게 되어
        //   `뒤처진 적`처럼 1위가 자주 바뀌는 정책이 사실상 죽는다. 반대로 버스트 안에서까지 매 발
        //   다시 고르면 "같은 조준으로 시간을 두고 여러 발"이라는 버스트의 정의가 깨진다.
        //   그 경계(사이클의 시작과 끝)를 아는 것은 발사 리듬을 굴리는 여기뿐이라 고정도 여기 산다.
        [NonSerialized] IDamageable burstTarget;

        // 이 타워가 애초에 쏠 수 있는가(저작이 갖춰졌는가). 조립 시 1회 판정한다 —
        // 판정 재료(수치·탄환 프리팹·비행 부품)가 전부 SO 값이라 Initialize 사이에 바뀌지 않는다.
        [NonSerialized] bool canFire;

        public override TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        // 최종 스탯 = SO 기본값 + 원장(Owner.Stats) 합성. 기본값만 여기가 알고, modifier는 원장이 소유한다.
        public float Damage =>
            fields == null ? 0f : Owner.Stats.Evaluate(TowerStat.AttackDamage, fields.AttackDamage);

        public float Range =>
            fields == null ? 0f : Owner.Stats.Evaluate(TowerStat.AttackRange, fields.AttackRange);

        // 공격속도는 배율 스탯이라 기본값 1f로 평가한다. 속도가 오를수록 간격이 짧아지므로 나눈다.
        public float Interval =>
            fields == null
                ? 0f
                : fields.AttackInterval / Mathf.Max(Owner.Stats.Evaluate(TowerStat.AttackSpeed, 1f), 0.01f);

        // 선택 사거리 원은 실제 교전 사거리를 그린다(원장 합성값 = 타일 버프·버프 오라 반영).
        public override float DisplayRange => Range;

        protected override void OnInitialize(TowerAsset asset)
        {
            fields = asset.Attack;
            effects = asset.Effects;
            enemyLayerMask = Owner.EnemyLayerMask;
            flight = fields?.Flight;   // SO의 부품을 그대로 쓴다 — 무상태라 공유해도 안전하다
            impact = BuildImpact(asset, enemyLayerMask);
            zone = asset.GroundZone;
            cooldownTimer = 0f;
            burstRemaining = 0;
            burstTarget = null;

            // TryAttack이 실패하는 조건과 **같은 것**을 미리 판정해 둔다(아래 Tick 주석 참조).
            canFire = fields != null && fields.ProjectilePrefab != null && flight != null;
        }

        public override void Dispose()
        {
            // 외부에 남기는 상태가 없다(투사체는 발사 후 독립). 쿨다운만 초기화해 재활성화 시 즉시 사격 가능.
            cooldownTimer = 0f;

            // 붙들던 대상은 놓는다 — 다음 밤에 이미 사라진 적을 물고 시작하지 않게(#387).
            burstRemaining = 0;
            burstTarget = null;
        }

        // 정보 패널에 이 액션이 기여할 줄: 공격력 / 사거리 / 공격속도.
        // 배치 전 툴팁(TowerTooltipView)과 같은 포매터를 쓴다 — 값의 출처만 다르다(원장 합성값 vs SO 원본).
        public override string DescribeStats()
        {
            if (fields == null) return null;

            string text = TowerStatsFormatter.BuildAttackLines(Damage, Range, Interval);

            // 조준 방식은 여기서 내지 않는다 — 인게임 전환이 붙으면서 정보 패널의 **전용 행**이
            // 소유하게 됐다(#387). 스탯 블록은 선택 시점 스냅샷이라 버튼을 눌러도 갱신되지 않아,
            // 여기 두면 조작 직후 두 표기가 어긋난다.
            return TowerStatsFormatter.Join(
                text,
                TowerStatsFormatter.BuildBurstLine(fields.BurstCount),
                zone != null && zone.IsAuthored
                    ? TowerStatsFormatter.BuildGroundZoneLine(zone.Radius, zone.Duration)
                    : null,
                DescribeEffects(effects, Owner));
        }

        /// 효과 목록을 설명 줄로 잇는다. 공격 액션과 디버프 오라가 같은 표기를 공유한다.
        ///
        /// ⚠ **적용부와 같은 술어로 걸러야 한다.** 합성 계승(#274 Phase 5)으로 꺼진 효과를 그대로 표기하면
        /// "정보 패널엔 독이 있다는데 실제로는 안 걸리는" 어긋남이 생긴다 — 표시부와 적용부가 규칙을 각자
        /// 쓰는 것이 WL-079/WL-130이 지적한 문제였다. 그래서 `stats`가 아니라 **`owner`를 통째로** 받는다:
        /// 원장과 필터가 같은 곳에서 나오므로 호출부가 필터를 빠뜨릴 수 없다.
        internal static string DescribeEffects(List<HitEffect> effects, Tower owner)
        {
            if (effects == null || effects.Count == 0 || owner == null) return null;

            string result = null;
            for (int i = 0; i < effects.Count; i++)
            {
                HitEffect effect = effects[i];
                if (effect == null) continue;
                if (!owner.IsEffectActive(effect.Kind)) continue;   // 계승으로 꺼진 효과는 표기하지 않는다

                string line = effect.Describe(owner.Stats);
                if (string.IsNullOrEmpty(line)) continue;
                result = result == null ? line : $"{result}\n{line}";
            }
            return result;
        }

        public override void Tick(float deltaTime)
        {
            // ⚠ **저작이 비어 있으면 Tick 자체에 들어가지 않는다.**
            //
            // 쿨다운은 `TryAttack`이 성공했을 때만 리셋된다. 그래서 쏠 수 없는 타워는 매 프레임
            // `cooldownTimer <= 0`인 채로 `Owner.AcquireTarget()`을 부르고, 그 안의
            // OverlapSphereNonAlloc이 초당 60번 돈다 — 아무것도 못 하면서 물리 예산만 태우는 것이다.
            // (`lightning_tower`류의 전 필드 0 SO가 배치되면 정확히 이 상태가 된다, WL-001.)
            //
            // 타워 수가 계속 늘어나는 장르라 "무동작이면 비용도 0"이어야 한다.
            if (!canFire) return;

            cooldownTimer -= deltaTime;
            if (cooldownTimer > 0f) return;

            // 연발 도중이면 사이클 첫 발의 대상을 그대로 쓰고, 사이클 경계에서만 호스트에게 다시 묻는다
            // (#387). 대상 **선정**은 여전히 호스트가 소유한다(`Tower.AcquireTarget`) — 포탑 조준 연출과
            // 같은 정의를 쓰기 위해서다. 여기가 정하는 것은 "누구를"이 아니라 "언제 다시 묻는가"뿐이다.
            bool reuseBurstTarget = burstRemaining > 0 && Owner.IsTargetValid(burstTarget, Range);
            IDamageable target = reuseBurstTarget ? burstTarget : Owner.AcquireTarget();

            if (target == null)
            {
                // 연발 도중 대상이 사라지면 사이클을 접는다. 남겨두면 다음 적이 사거리에 들어온 순간
                // 남은 발이 간격 없이 몰아서 나간다 — 연발 리듬이 적의 등장 타이밍에 좌우된다.
                burstRemaining = 0;
                burstTarget = null;
                return;
            }

            if (!TryAttack(target)) return;

            // 사이클의 첫 발이면 남은 발수를 채우고, 이어지는 발이면 하나 줄인다.
            // BurstCount 기본 1이면 첫 발에서 0이 되어 곧바로 Interval로 떨어진다 = 기존 거동.
            if (burstRemaining <= 0) burstRemaining = Mathf.Max(1, fields.BurstCount) - 1;
            else burstRemaining--;

            // 붙들던 대상을 못 쓴 발은 **새로 고른 대상을 남은 발이 이어받는다.**
            // 이 대입이 첫 발 분기 안에만 있으면, 연발 도중 대상이 죽었을 때 죽은 참조가 사이클 끝까지
            // 남아 남은 발이 매번 따로 재조준한다 — "버스트 = 같은 조준"이 대상 사망 시에만 깨진다.
            //
            // 사이클을 접는 쪽(burstRemaining = 0)이 아니라 이어받는 쪽을 고른 이유: 접으면 적을 죽인
            // 순간 남은 발이 사라지고 정규 간격으로 돌아가, **잘 죽인 것이 손해가 된다.**
            if (!reuseBurstTarget) burstTarget = target;

            // 사이클이 끝났으면 붙들던 대상을 놓는다 — 다음 첫 발은 정책이 새로 고른다.
            if (burstRemaining <= 0) burstTarget = null;

            // 연발이 남았으면 짧은 간격, 사이클이 끝났으면 정규 공격 간격(원장 경유).
            cooldownTimer = burstRemaining > 0
                ? Mathf.Max(fields.BurstInterval, 0.02f)   // 0 이하 폭주 방지 하한
                : Interval;
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;
            if (fields == null || fields.ProjectilePrefab == null) return false;
            if (flight == null) return false;   // 비행 부품 미저작 — TowerAsset.OnValidate가 저장 시점에 경고한다

            // 포구가 여럿이면(양날개 터렛 등) 발사마다 번갈아 쓴다 — 커서는 호스트가 굴린다(#336).
            // firePoint 미할당이면 타워 루트(바닥)에서 생성 — 하위 호환(ArcherTower.prefab이 그렇다).
            Transform firePoint = Owner.NextFirePoint();
            Vector3 spawnPos = firePoint != null ? firePoint.position : Origin.position;

            // 대상을 향한 회전을 항상 만든다 — Homing/Ballistic은 스폰 회전을 안 쓰므로(첫 Update에서
            // 이동 방향으로 즉시 덮어써 렌더링 전에 사라진다) 회귀 위험이 없고, StraightFlight/
            // BoomerangFlight처럼 "발사 순간 회전을 방향으로 고정"하는 비행 방식은 PelletCount==1이어도
            // (부메랑이 그렇다, #298) 대상을 조준해야 한다 — 예전엔 PelletCount>1일 때만 계산해서
            // 산탄 아닌 단발 직선형 비행이 항상 월드 +Z로 나가는 버그가 있었다.
            // ★ **Y 성분을 버리지 않는다.** 예전에는 `aimDir.y = 0f`로 조준을 완전 수평으로 만들었는데,
            // 이 맵의 지형이 그것을 허용하지 않는다 — 타워가 설 수 있는 Grass 타일의 윗면
            // (`BattleTile.AnchorPosition.y`)은 3.80이고 몬스터가 걷는 Road 타일 윗면은 0.80이다.
            // 즉 타워는 **항상 3.00 높은 곳**에 서고, 사거리 버프 타일(`BT_Range_1/2/3`)에 올리면
            // 앵커가 5.00·6.20·7.40으로 1.20씩 더 올라간다. 수평으로 쏘면 그 높이의 평면을 훑으므로
            // 몬스터 콜라이더 상단보다 위를 지나가 **전탄이 빗나간다** — 실측(2026-08-18):
            //
            //   머즐 y      6.24(일반) / 7.44(Range_1) / 8.64(Range_2) / 9.84(Range_3)
            //   Flying_Bat  콜라이더 상단 5.05 → 일반 타일에서도 100% 미스
            //   Yellow·Red  상단 6.51·6.67 → 일반 타일은 정수리를 스치고, 버프 타일에서 100% 미스
            //
            // ⚠ 위 수치는 **StartMap 기하를 월드 좌표로 잰 값**이다(`AnchorPosition`이 월드다).
            // `CombatBalance.md`가 같은 지형을 "도로 top 3 / 웨이포인트 6"으로 적는 것과 숫자가
            // 다른 이유는 그쪽이 battlespace 로컬 기준이기 때문이다 — 두 좌표계 사이에 변환 유틸이
            // 없고(WL-007) 부양값이 `monsterWaypointYOffset = 6f` 매직 상수라(WL-063) 아직 단일
            // 출처가 없다. **여기 수치로 다른 시스템(스킬 반경 등)을 검산하지 말 것.** 절차 생성
            // 맵은 상대 높이가 다를 수 있으나, 3D 조준은 높이차 값에 의존하지 않으므로 어느
            // 쪽에서도 옳게 동작한다.
            //
            // 대상을 획득해 발사까지 하면서 데미지만 0이라 원인이 드러나지 않는 자리였다.
            // Y를 살리면 언덕 위에서 움푹한 길을 내려다보고 쏘는 실제 지형과 조준이 일치한다.
            //
            // ⚠ 이 값을 쓰는 것은 **`HitPosition`이다** — 그래서 그 필드가 조준의 정본이 됐다.
            // 몬스터 프리팹에서 몸통 중심(콜라이더 안)을 가리키지 않으면 땅바닥이나 몸 아래를 쏜다.
            // 미할당 시 `Enemy.Awake`가 피벗으로 폴백하는데, 피벗은 박쥐처럼 떠 있는 적에서는
            // 자기 콜라이더 **밖**이다 — Homing/Chain도 같은 필드를 읽으므로 저작은 공통 전제다.
            Transform aimAt = target.HitPosition;
            Vector3 aimDir = aimAt != null ? aimAt.position - spawnPos : Vector3.forward;
            Quaternion baseRotation = aimDir.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(aimDir.normalized)
                : Quaternion.identity;

            int pelletCount = Mathf.Max(1, fields.PelletCount);
            bool anyFired = false;
            float half = fields.SpreadAngle * 0.5f;
            for (int i = 0; i < pelletCount; i++)
            {
                // 부채꼴 균등 분할: -half ~ +half. 회전에 오프셋을 실어 보내므로 StraightFlight는
                // 대상을 몰라도 그 방향 그대로 직진하기만 하면 된다(무상태 규칙 유지).
                //
                // 오프셋이 `baseRotation` **뒤에** 곱해지므로 조준축을 기준으로 한 좌우 분할이다 —
                // 앙각이 실린 뒤에도 부채꼴이 조준선을 감싸는 원뿔로 유지된다. 대신 3발이 **같은
                // 앙각을 공유**하므로 한 발은 한 높이에만 커밋된다. 지금 기하에서는 무해하다
                // (박쥐 조준점 4.24 ≈ 레드그루미 4.24로 층 차이가 콜라이더 세로 폭보다 훨씬 작다)
                // — 고도가 확연히 다른 적이 추가되면 그때 세로 판정 여유가 필요해진다.
                float angle = pelletCount == 1 ? 0f : Mathf.Lerp(-half, half, i / (float)(pelletCount - 1));
                Quaternion rotation = angle == 0f ? baseRotation : baseRotation * Quaternion.Euler(0f, angle, 0f);

                if (SpawnPellet(spawnPos, rotation, target)) anyFired = true;
            }

            if (!anyFired) return false;

            // 발사 1회당 1번 — 펠릿 수와 무관하다(#298).
            Owner.RaiseFired();
            return true;
        }

        bool SpawnPellet(Vector3 spawnPos, Quaternion rotation, IDamageable target)
        {
            GameObject obj = UnityEngine.Object.Instantiate(fields.ProjectilePrefab, spawnPos, rotation);

            if (!obj.TryGetComponent(out Projectile projectile))
            {
                UnityEngine.Object.Destroy(obj);   // Projectile 컴포넌트 없으면 스폰물 제거 후 실패
                return false;
            }

            // 착탄 구역(#336) — 저작돼 있으면 이 탄 한 발의 착탄 통지에 붙는다. 구독자는 탄과 함께
            // 사라지므로 해제 책임이 없다. `Init`보다 **먼저** 붙는 이유는 비행 부품이 Begin 시점에
            // 곧바로 접촉을 보고할 수 있어서다(근접 발사).
            if (zone != null && zone.IsAuthored) SubscribeGroundZone(projectile);

            // 데미지 소스는 Owner다 — IAttacker 계약을 가진 쪽이 타워이므로 DamageInfo가 타워를 가리킨다.
            // Range(원장 합성값, 사거리 버프 반영)도 함께 넘긴다 — StraightFlight/BoomerangFlight가
            // 이 값을 실제 비행 거리로 쓴다(#298).
            projectile.Init(target, Damage, Owner, flight, impact, effects, Range);
            return true;
        }

        /// 펠릿 1발에 착탄 구역 스폰을 걸어 둔다.
        ///
        /// ⚠ **탄 1발당 구역 1개로 제한한다.** `Projectile.Impacted`는 관통·부메랑처럼 여러 번 때리는 탄에서
        /// 명중마다 발행되므로, 걸러내지 않으면 왕복 한 번에 구역이 여러 개 쌓인다. 그 조합은 지금
        /// 저작되어 있지 않지만, 나중에 저작되는 순간 조용히 화력이 몇 배가 되는 자리다.
        void SubscribeGroundZone(Projectile projectile)
        {
            // 펠릿마다 별도 지역 변수라 산탄에서도 발마다 하나씩 생긴다.
            bool spawned = false;

            projectile.Impacted += impactPos =>
            {
                if (spawned) return;
                spawned = true;

                GroundZone.Spawn(zone, impactPos, Owner, effects);
            };
        }

        // "어떻게 날아갈지". 전부 SO가 정한다 — 예전에는 Speed만 SO였고 Mode/ArcHeight는 탄환 프리팹에
        // 박혀 있어, 같은 궤적을 만드는 값이 두 파일로 갈려 있었다(#274).
        // "터지면 누구를 때릴지". 발사마다 재구성할 이유가 없어 조립 시 1회 만든다
        // (ProjectileImpact는 struct라 발사 시 복사되어 전달된다).
        static ProjectileImpact BuildImpact(TowerAsset asset, LayerMask enemyLayerMask)
        {
            ProjectileImpact result = asset.Impact switch
            {
                ImpactKind.Area => ProjectileImpact.MakeArea(asset.SplashRadius, enemyLayerMask),
                ImpactKind.Chain => ProjectileImpact.MakeChain(
                    asset.ChainRadius, asset.MaxChainTargets, asset.ChainDamageFalloff, enemyLayerMask),
                _ => ProjectileImpact.MakeSingle(),
            };

            // 명중 효과(스턴 등)는 여기 섞지 않는다 — Projectile이 세 경로 공통 지점에서 Effects를 적용한다.
            return result;
        }

#if UNITY_EDITOR
        public override void DrawGizmos()
        {
            if (fields == null) return;
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(Origin.position, Vector3.up, Range);
        }
#endif
    }
}
