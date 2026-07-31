using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // 즉시 판정(히트스캔)으로 공격하는 타워의 행동. 현재 체인 타워가 쓴다(#252).
    //
    // AttackBehaviour(투사체)와 **전달 축**만 다르다. 발사와 명중이 같은 프레임에 끝나므로
    // "비행 중 첫 대상 사망" 창이 존재하지 않는다 — 투사체 체인이 그 창에서 홉 전체를 잃던
    // 경로(Projectile의 Homing 조기 파괴 / Ballistic 착탄 시 생존 검사)가 구조적으로 사라진다.
    // 명중 규칙 자체는 ChainResolver가 소유하며 투사체 경로와 공유한다.
    //
    // AttackBehaviour와 대상 탐색·쿨다운·스탯 골격이 겹치지만 공통 기반 클래스를 두지 않는다 —
    // BuffAura/DebuffAura가 같은 이유로 독립 sealed 클래스인 것과 같은 관례다(공유할 **규칙**은
    // static 헬퍼로 빼고, 골격은 각자 소유). 상속을 끼우면 행동 하나를 읽을 때 부모까지 따라가야 한다.
    [AddComponentMenu("")]   // 런타임 조립 전용 — Add Component 메뉴에 노출하지 않는다
    [DisallowMultipleComponent]
    public sealed class HitscanAttackBehaviour : MonoBehaviour, IAttackBehaviour
    {
        Tower owner;
        TowerAsset.AttackFields fields;
        TowerAsset.ChainFields chain;

        // 대상 탐색용 마스크. 투사체 경로와 달리 명중 판정도 이 마스크로 하므로 ChainResolver에 그대로 넘긴다.
        LayerMask enemyLayerMask;

        // 빔 시작점(포신/머즐). 투사체 생성 위치와 같은 값을 쓴다 — 같은 타워에서 투사체와 빔이
        // 다른 곳에서 나가면 안 되고, 미할당 시 폴백도 AttackBehaviour와 동일하다.
        Transform firePoint;

        float cooldownTimer;
        readonly Collider[] hitBuffer = new Collider[16];

        // 홉 좌표 수집 버퍼(재사용). ChainResolver가 채우고 빔이 그대로 소비한다 —
        // 표시부가 경로를 다시 계산하지 않으므로 "선이 잇는 대상"과 "피해 대상"이 어긋날 수 없다.
        readonly List<Vector3> beamPath = new List<Vector3>();

        // 빔 연출 기본값. BeamPrefab이 없을 때만 쓰인다(있으면 프리팹 저작이 외형을 결정).
        // 정식 스펙은 아트 협의 대상이라 검증용 임시값이다(#252 열린 결정).
        const float k_BeamLifetime = 0.15f;
        static readonly Color k_BeamColor = new Color(0.6f, 0.85f, 1f, 0.9f);

        public TowerActivePhase ActivePhase => TowerActivePhase.NightOnly;

        // 최종 스탯 = SO 기본값 + 원장(owner.Stats) 합성. AttackBehaviour와 동일한 규칙을 쓴다 —
        // 타일 버프·버프 오라·플레이어 스킬이 전달 방식과 무관하게 먹혀야 하기 때문.
        public float Damage =>
            fields == null ? 0f : owner.Stats.Evaluate(TowerStat.AttackDamage, fields.AttackDamage);

        public float Range =>
            fields == null ? 0f : owner.Stats.Evaluate(TowerStat.AttackRange, fields.AttackRange);

        // 공격속도는 배율 스탯이라 기본값 1f로 평가한다. 속도가 오를수록 간격이 짧아지므로 나눈다.
        public float Interval =>
            fields == null
                ? 0f
                : fields.AttackInterval / Mathf.Max(owner.Stats.Evaluate(TowerStat.AttackSpeed, 1f), 0.01f);

        public float DisplayRange => Range;

        // 빔 수명을 공격 간격보다 짧게 묶는다 — 공속 버프로 Interval이 짧아졌을 때 자기 빔이 겹쳐 보이지 않게.
        float BeamLifetime
        {
            get
            {
                float interval = Interval;
                return interval > 0f ? Mathf.Min(k_BeamLifetime, interval * 0.8f) : k_BeamLifetime;
            }
        }

        public void Initialize(in TowerBuildContext context)
        {
            owner = context.Owner;
            fields = TowerBehaviourFactory.ResolveAttackFields(context.Asset);
            chain = context.Asset.Chain;
            enemyLayerMask = context.EnemyLayerMask;
            firePoint = context.FirePoint;
            cooldownTimer = 0f;
        }

        // 빔 시작점. 미할당이면 타워 루트에서 나간다(AttackBehaviour의 투사체 생성 폴백과 동일).
        Vector3 FirePosition => firePoint != null ? firePoint.position : transform.position;

        public void Dispose()
        {
            // 외부에 남기는 상태가 없다 — 판정이 그 프레임에 끝나고, 부여한 효과는 대상이 소유한다.
            // 쿨다운만 초기화해 재활성화 시 즉시 사격 가능.
            cooldownTimer = 0f;
        }

        // 정보 패널 기여 줄. 투사체 타워와 같은 포매터를 쓴다 — 플레이어에게는 전달 방식이 아니라
        // 공격력/사거리/공격속도가 보여야 하고, 그 서식이 갈리면 패널이 타워마다 달라 보인다(WL-079).
        public string DescribeStats()
            => fields == null ? null : TowerStatsFormatter.BuildAttackLines(Damage, Range, Interval);

        public void Tick(float deltaTime)
        {
            if (fields == null) return;

            cooldownTimer -= deltaTime;
            if (cooldownTimer > 0f) return;

            IDamageable target = FindTarget();
            if (target != null && TryAttack(target)) cooldownTimer = Interval;
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;
            if (chain == null) return false;

            // 이전 발사의 경로가 남지 않도록 먼저 비운다 — Resolve가 조기 반환하면 채우지 않으므로,
            // 비우지 않으면 지난 발사의 빔을 다시 그릴 수 있다.
            beamPath.Clear();

            // 데미지 소스는 owner다 — IAttacker 계약을 가진 쪽이 타워이므로 DamageInfo가 타워를 가리킨다.
            ChainResolver.Resolve(
                target, Damage, owner,
                chain.ChainRadius, chain.MaxChainTargets, chain.ChainDamageFalloff, enemyLayerMask,
                beamPath);

            ChainBeamVisual.Spawn(chain.BeamPrefab, FirePosition, beamPath, BeamLifetime, k_BeamColor);

            owner.RaiseFired();
            return true;
        }

        // 사거리 내 가장 가까운 적을 타겟으로 선정 (매 프레임 경로라 NonAlloc 유지)
        IDamageable FindTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, Range, hitBuffer, enemyLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider hit = hitBuffer[i];
                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.Faction != owner.Faction && !damageable.IsDead)
                {
                    float sqrDistance = (hit.transform.position - transform.position).sqrMagnitude;
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
        void OnDrawGizmosSelected()
        {
            if (fields == null) return;
            // 투사체 타워(빨강)와 구분되도록 하늘색 — 씬에서 전달 방식이 한눈에 보인다.
            UnityEditor.Handles.color = new Color(0.4f, 0.8f, 1f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, Range);
        }
#endif
    }
}
