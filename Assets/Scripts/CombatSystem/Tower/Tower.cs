using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NorthLand.Combat
{
    public class Tower : MonoBehaviour, IAttacker
    {
        [SerializeField] TowerAsset data;

        // TODO(TBD): 대상 탐지 필터링을 LayerMask로 할지 Tag로 할지 미확정. 임시 LayerMask.
        [SerializeField] LayerMask enemyLayerMask;

        // 투사체 생성 위치(포신/머즐). 미할당 시 기존처럼 타워 루트(바닥)에서 생성(하위 호환).
        [SerializeField] Transform firePoint;

        float cooldownTimer;
        readonly Collider[] hitBuffer = new Collider[16];

        // 버프 스킬(#103)이 이 배율만 조작한다 — 공유 TowerAsset 값은 건드리지 않음(타입 전체가 아니라
        // 이 인스턴스만 일시적으로 강화됨). AuraTower(Magic)는 AttackFields 자체가 없어 버프 대상 아님.
        float damageMultiplier = 1f;
        float attackSpeedMultiplier = 1f;
        CancellationTokenSource buffCts;

        // 씬에 존재하는 모든 Tower를 스킬 등에서 순회할 수 있게 자가 등록(FindObjectsByType 대체).
        public static readonly List<Tower> Active = new();
        void OnEnable() => Active.Add(this);

        // 발사 시점 통지(탄약 시각 연출 등 구독용 — 예: 캐논 포탄이 발사 순간 사라짐).
        public event Action OnFired;

        void OnDisable()
        {
            Active.Remove(this);
            buffCts?.Cancel();
            buffCts?.Dispose();
            buffCts = null;
            // 취소된 BuffRoutine은 원복 코드를 지나지 않고 바로 return하므로 여기서 직접 리셋해야
            // 풀링 등으로 재활성화됐을 때 버프가 고착되지 않는다(PR#115 리뷰 지적).
            damageMultiplier = 1f;
            attackSpeedMultiplier = 1f;
        }

        public Faction Faction => Faction.Player;

        // TowerType에 맞는 공통 공격 스탯 해석. Magic(또는 data 미할당)은 Attack 없음 → null.
        TowerAsset.AttackFields Attack => data == null ? null : data.TowerType switch
        {
            TowerType.Single => data.Single.Attack,
            TowerType.Area => data.Area.Attack,
            TowerType.Chain => data.Chain.Attack,
            _ => null,
        };

        // Magic 타워/미할당(Attack==null)에서도 안전하도록 null 가드(공개 IAttacker 계약).
        public float AttackDamage => Attack != null ? Attack.AttackDamage * damageMultiplier : 0f;
        public float AttackRange => Attack != null ? Attack.AttackRange : 0f;
        // 공격속도 배율이 클수록 더 빠르게(간격이 짧아짐) 공격하도록 나눗셈으로 적용.
        public float AttackInterval => Attack != null ? Attack.AttackInterval / attackSpeedMultiplier : 0f;

        // 버프 스킬(#103) 진입점. 지속시간 동안 배율 적용 후 자동 원복. 재시전 시 남은 지속시간을 새 값으로 갱신.
        // AuraTower.AuraLoop와 동일한 UniTask+CancellationTokenSource 패턴(코루틴 대신).
        public void ApplyBuff(float damageMul, float attackSpeedMul, float duration)
        {
            buffCts?.Cancel();
            buffCts?.Dispose();
            buffCts = new CancellationTokenSource();
            BuffRoutine(damageMul, attackSpeedMul, duration, buffCts.Token).Forget();
        }

        async UniTaskVoid BuffRoutine(float damageMul, float attackSpeedMul, float duration, CancellationToken ct)
        {
            damageMultiplier = damageMul;
            attackSpeedMultiplier = attackSpeedMul;

            bool canceled = await UniTask
                .Delay(TimeSpan.FromSeconds(duration), cancellationToken: ct)
                .SuppressCancellationThrow();
            if (canceled) return;

            damageMultiplier = 1f;
            attackSpeedMultiplier = 1f;
        }

        void Update()
        {
            // 공격 스탯이 없는 타입(Magic 등)은 이 컴포넌트가 처리하지 않음
            if (Attack == null) return;
            if (DayNightManager.Instance != null &&
                DayNightManager.Instance.CurrentPhase != DayNightManager.Phase.Night) return;

            cooldownTimer -= Time.deltaTime;
            if (cooldownTimer > 0f) return;

            var target = FindTarget();
            if (target != null && TryAttack(target))
                cooldownTimer = AttackInterval;
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;

            var atk = Attack;
            if (atk == null || atk.ProjectilePrefab == null) return false;

            Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
            var obj = Instantiate(atk.ProjectilePrefab, spawnPos, Quaternion.identity);
            if (!obj.TryGetComponent<Projectile>(out var projectile))
            {
                Destroy(obj);   // Projectile 컴포넌트 없으면 스폰물 제거 후 실패
                return false;
            }

            // 타입별 명중 동작(단일/스플래시/체인)을 구성해 투사체에 전달
            projectile.Init(target, atk.AttackDamage, atk.ProjectileSpeed, this, BuildImpact());
            OnFired?.Invoke();
            return true;
        }

        ProjectileImpact BuildImpact()
        {
            switch (data.TowerType)
            {
                case TowerType.Area:
                    return ProjectileImpact.MakeArea(data.Area.SplashRadius, enemyLayerMask);
                case TowerType.Chain:
                    var c = data.Chain;
                    return ProjectileImpact.MakeChain(
                        c.ChainRadius, c.MaxChainTargets, c.ChainDamageFalloff, enemyLayerMask);
                default:
                    return ProjectileImpact.MakeSingle();
            }
        }

        // 사거리 내 가장 가까운 적을 타겟으로 선정 (매 프레임 경로라 NonAlloc 유지)
        IDamageable FindTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, AttackRange, hitBuffer, enemyLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                var damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.Faction != Faction && !damageable.IsDead)
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
            if (Attack == null) return;
            Handles.color = Color.red;
            Handles.DrawWireDisc(transform.position, Vector3.up, Attack.AttackRange);
        }
#endif
    }
}