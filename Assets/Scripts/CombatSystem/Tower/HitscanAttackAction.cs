using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 즉시 판정(히트스캔)으로 공격하는 액션. 현재 체인 타워가 쓴다(#252).
    //
    // `AttackAction`(투사체)와 **전달 축**만 다르다. 발사와 명중이 같은 프레임에 끝나므로
    // "비행 중 첫 대상 사망" 창이 존재하지 않는다 — 투사체 체인이 그 창에서 홉 전체를 잃던
    // 경로(Homing 조기 파괴 / Ballistic 착탄 시 생존 검사)가 구조적으로 사라진다.
    // 명중 규칙 자체는 `ChainResolver`가 소유한다.
    //
    // `AttackAction`과 대상 탐색·쿨다운·스탯 골격이 겹치지만 공통 기반 클래스를 두지 않는다 —
    // BuffAura/DebuffAura가 같은 이유로 독립 sealed 클래스인 것과 같은 관례다(공유할 **규칙**은
    // static 헬퍼로 빼고, 골격은 각자 소유). 공유하는 것은 능력 계약(`IAttackAction`)뿐이다.
    //
    // ⚠ **명중 효과(TowerAsset.Effects)를 걸지 않는다** — 체인은 스턴·화상을 받지 않는 것으로 확정됐다(#252).
    //   ① 테마: 번개(전격)라 발화·기절이 성립하지 않는다.
    //   ② 수치: 1발에 최대 `MaxChainTargets`명을 때리므로, 그대로 걸면 CC/DoT 처리량이 단일 타격 기준으로
    //      잡은 튜닝을 무너뜨린다.
    //   지원하려면 `ChainResolver.Resolve`에 효과 축을 더해 홉마다 부여하는 형태여야 하고 기획 결정이
    //   선행한다. 그때까지 저작해도 무시되므로 `TowerAsset.OnValidate`가 저장 시점에 경고한다.
    [Serializable]
    public sealed class HitscanAttackAction : TowerAction, IAttackAction
    {
        // ── 런타임 상태 (직렬화 금지 — TowerAction 규칙 ③) ──────────────────
        [NonSerialized] TowerAsset.AttackFields fields;

        // 체인 수치는 SO를 통해 읽는다 — 값을 복사해 두면 플레이 중 SO 튜닝이 반영되지 않는다.
        // `Attack`을 참조로 캐싱하는 AttackAction과 같은 성질을 평탄 필드에서 얻는 방법이다.
        [NonSerialized] TowerAsset asset;

        // 대상 탐색용 마스크. 투사체 경로와 달리 명중 판정도 이 마스크로 하므로 ChainResolver에 그대로 넘긴다.
        [NonSerialized] LayerMask enemyLayerMask;

        [NonSerialized] float cooldownTimer;
        [NonSerialized] Collider[] hitBuffer;

        // 홉 좌표 수집 버퍼(재사용). ChainResolver가 채우고 빔이 그대로 소비한다 —
        // 표시부가 경로를 다시 계산하지 않으므로 "선이 잇는 대상"과 "피해 대상"이 어긋날 수 없다.
        [NonSerialized] List<Vector3> beamPath;

        // 체인 해결기는 **타워마다 하나**다. 순회 상태(중복 방지 집합)를 공유하면, 명중 통지 구독자가
        // 같은 콜스택에서 다른 타워를 발사시킬 때 진행 중인 순회가 오염된다(ChainResolver 주석 참조).
        // 직렬화하지 않고 여기서 만드는 것이 곧 "타워별 독립"을 보장하는 경로다(규칙 ③).
        [NonSerialized] ChainResolver resolver;

        // 이 타워가 애초에 쏠 수 있는가. AttackAction과 같은 이유로 조립 시 1회 판정한다 —
        // 판정하지 않으면 사거리 0인 미저작 SO가 초당 60번 OverlapSphere를 돌며 물리 예산만 태운다
        // (`lightning_tower`류의 전 필드 0 SO가 정확히 이 상태였다, WL-001).
        [NonSerialized] bool canFire;

        // 빔 연출 기본값. BeamPrefab이 없을 때만 쓰인다(있으면 프리팹 저작이 외형을 결정).
        // 정식 스펙은 아트 협의 대상이라 검증용 임시값이다(#252 열린 결정).
        const float k_BeamLifetime = 0.15f;
        static readonly Color k_BeamColor = new Color(0.6f, 0.85f, 1f, 0.9f);

        public override TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        // 최종 스탯 = SO 기본값 + 원장(Owner.Stats) 합성. AttackAction과 동일한 규칙을 쓴다 —
        // 타일 버프·버프 오라·플레이어 스킬이 전달 방식과 무관하게 먹혀야 하기 때문.
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

        // 빔 수명을 공격 간격보다 짧게 묶는다 — 공속 버프로 Interval이 짧아졌을 때 자기 빔이 겹쳐 보이지 않게.
        float BeamLifetime
        {
            get
            {
                float interval = Interval;
                return interval > 0f ? Mathf.Min(k_BeamLifetime, interval * 0.8f) : k_BeamLifetime;
            }
        }

        // 빔 시작점. 미할당이면 타워 루트에서 나간다(AttackAction의 투사체 생성 폴백과 동일) —
        // 같은 타워에서 투사체와 빔이 다른 곳에서 나가면 안 된다.
        Vector3 FirePosition
        {
            get
            {
                Transform firePoint = Owner.FirePoint;
                return firePoint != null ? firePoint.position : Origin.position;
            }
        }

        protected override void OnInitialize(TowerAsset asset)
        {
            this.asset = asset;
            fields = asset.Attack;
            enemyLayerMask = Owner.EnemyLayerMask;
            cooldownTimer = 0f;

            // 사거리 0이면 어떤 대상도 찾을 수 없다 — 무동작이면 비용도 0이어야 한다(위 canFire 주석).
            canFire = fields != null && fields.AttackRange > 0f;

            // 매 프레임 경로라 NonAlloc 버퍼를 쓴다. 직렬화되지 않으므로 여기서 만든다.
            hitBuffer ??= new Collider[16];
            beamPath ??= new List<Vector3>();
            resolver ??= new ChainResolver();
        }

        public override void Dispose()
        {
            // 외부에 남기는 상태가 없다 — 판정이 그 프레임에 끝나고, 부여한 효과는 대상이 소유한다.
            // 쿨다운만 초기화해 재활성화 시 즉시 사격 가능.
            cooldownTimer = 0f;
        }

        // 정보 패널 기여 줄. 투사체 타워와 같은 포매터를 쓴다 — 플레이어에게는 전달 방식이 아니라
        // 공격력/사거리/공격속도가 보여야 하고, 그 서식이 갈리면 패널이 타워마다 달라 보인다(WL-079).
        //
        // AttackAction과 달리 `DescribeEffects`를 붙이지 않는다. 이 액션이 효과를 걸지 않으므로
        // 표기하면 "패널엔 화상이 있는데 실제로는 안 걸리는" 어긋남이 된다 — 표시부와 적용부가 같은
        // 술어를 쓴다는 규약(WL-079/WL-130)을 지키는 쪽이 이 방향이다.
        public override string DescribeStats()
            => fields == null ? null : TowerStatsFormatter.BuildAttackLines(Damage, Range, Interval);

        public override void Tick(float deltaTime)
        {
            if (!canFire) return;

            cooldownTimer -= deltaTime;
            if (cooldownTimer > 0f) return;

            IDamageable target = FindTarget();
            if (target != null && TryAttack(target)) cooldownTimer = Interval;
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;
            if (asset == null) return false;

            // 이전 발사의 경로가 남지 않도록 먼저 비운다 — Resolve가 조기 반환하면 채우지 않으므로,
            // 비우지 않으면 지난 발사의 빔을 다시 그릴 수 있다.
            beamPath.Clear();

            // 데미지 소스는 Owner다 — IAttacker 계약을 가진 쪽이 타워이므로 DamageInfo가 타워를 가리킨다.
            resolver.Resolve(
                target, Damage, Owner,
                asset.ChainRadius, asset.MaxChainTargets, asset.ChainDamageFalloff, enemyLayerMask,
                beamPath);

            ChainBeamVisual.Spawn(asset.BeamPrefab, FirePosition, beamPath, BeamLifetime, k_BeamColor);

            Owner.RaiseFired();
            return true;
        }

        // 사거리 내 가장 가까운 적을 타겟으로 선정 (매 프레임 경로라 NonAlloc 유지)
        IDamageable FindTarget()
        {
            Vector3 origin = Origin.position;
            int count = Physics.OverlapSphereNonAlloc(origin, Range, hitBuffer, enemyLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider hit = hitBuffer[i];
                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.Faction != Owner.Faction && !damageable.IsDead)
                {
                    float sqrDistance = (hit.transform.position - origin).sqrMagnitude;
                    if (sqrDistance < closestSqrDistance)
                    {
                        closestSqrDistance = sqrDistance;
                        closest = damageable;
                    }
                }
            }
            return closest;
        }

#if UNITY_EDITOR
        public override void DrawGizmos()
        {
            if (fields == null) return;
            // 투사체 타워(빨강)와 구분되도록 하늘색 — 씬에서 전달 방식이 한눈에 보인다.
            UnityEditor.Handles.color = new Color(0.4f, 0.8f, 1f);
            UnityEditor.Handles.DrawWireDisc(Origin.position, Vector3.up, Range);
        }
#endif
    }
}
