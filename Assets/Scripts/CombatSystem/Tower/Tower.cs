using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NorthLand.Combat
{
    public class Tower : MonoBehaviour, IAttacker, ISelectable
    {
        [SerializeField] TowerAsset data;

        // TODO(TBD): 대상 탐지 필터링을 LayerMask로 할지 Tag로 할지 미확정. 임시 LayerMask.
        [SerializeField] LayerMask enemyLayerMask;

        // 투사체 생성 위치(포신/머즐). 미할당 시 기존처럼 타워 루트(바닥)에서 생성(하위 호환).
        [SerializeField] Transform firePoint;

        float cooldownTimer;
        readonly Collider[] hitBuffer = new Collider[16];

        // 소스별 버프를 합산 중첩한다(#164). 각 소스(플레이어 버프 스킬, 각 버프 타워 종류)가 자기 항목을
        // add/refresh하고, 실효 배율 = 1 + 모든 소스 보너스의 합. 같은 소스키는 갱신만 되어 자기 자신과는
        // 중첩되지 않는다(버프 타워는 TowerID 해시를 소스키로 써 같은 종류끼리 미중첩, AuraTower와 규칙 일치).
        // 공유 TowerAsset 값은 건드리지 않고 이 인스턴스 배율만 조작한다. AuraTower(Magic)는 AttackFields가 없어 버프 대상 아님.
        struct BuffEntry { public float DamageBonus; public float SpeedBonus; public float Expiry; }
        readonly Dictionary<int, BuffEntry> activeBuffs = new();
        readonly List<int> expiredScratch = new();
        float damageMultiplier = 1f;
        float attackSpeedMultiplier = 1f;

        // 씬에 존재하는 모든 Tower를 스킬 등에서 순회할 수 있게 자가 등록(FindObjectsByType 대체).
        public static readonly List<Tower> Active = new();
        // 타워가 씬에 추가/제거될 때 발생. 버프 타워가 배치 즉시(낮 포함) + layout 변화 시 사거리 내 타워를 갱신하는 데 쓴다(#164).
        public static event Action ActiveChanged;
        void OnEnable() { Active.Add(this); ActiveChanged?.Invoke(); }

        // 발사 시점 통지(탄약 시각 연출 등 구독용 — 예: 캐논 포탄이 발사 순간 사라짐).
        public event Action OnFired;

        void OnDisable()
        {
            Active.Remove(this);
            ActiveChanged?.Invoke();   // 철거 등으로 빠지면 버프 타워가 대상 목록을 다시 계산하도록 통지.
            // 풀링 등으로 재활성화됐을 때 버프가 고착되지 않도록 활성 버프를 비우고 배율을 리셋한다(PR#115 리뷰 지적).
            activeBuffs.Clear();
            damageMultiplier = 1f;
            attackSpeedMultiplier = 1f;
        }

        public Faction Faction => Faction.Player;

        // 정보 패널 연동(#153) — BuildingInfo/BuildingInfoUI와 동일 패턴.
        // data.Data는 원래 TowerSelectPanelView가 배치 전 채워두지만(TowerPlacer 참고),
        // 그 경로를 거치지 않은 인스턴스(테스트 씬 등)를 위해 방어적으로 한 번 더 채운다.
        public void OnSelected()
        {
            if (data == null)
            {
                Debug.LogError("[Tower] TowerAsset 미할당", this);
                return;
            }

            data.Data ??= DataTableManager.Get<TowerTable>("TowerTable").Get(data.TowerID);
            if (data.Data == null)
            {
                Debug.LogError($"[Tower] TowerData 없음 (TowerID={data.TowerID})", this);
                return;
            }

            TowerInfoUI.Instance.ShowInfo(data.Data.DescriptionKey, BuildStatsText());
        }

        public void OnDeselected() => TowerInfoUI.Instance.HideInfo();

        // 공통 공격 스탯(공격력/사거리/공격속도)을 정보 패널용 평문으로 조합한다. Magic 타워는 공통 Attack이
        // 없어 null 반환(패널은 통계 구간 없이 설명만 표시). 라벨은 game.tower.* 로컬라이즈 키(k_DefaultTable)에서 가져온다.
        string BuildStatsText()
        {
            if (Attack == null) return null;

            string damageLabel = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, "game.tower.attack_damage");
            string rangeLabel = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, "game.tower.attack_range");
            string speedLabel = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, "game.tower.attack_speed");

            return $"{damageLabel}: {AttackDamage:0.#}\n{rangeLabel}: {AttackRange:0.#}\n{speedLabel}: {1f / AttackInterval:0.##}";
        }

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

        // 버프 진입점(플레이어 스킬 #103 / 버프 타워 #164 공용). sourceId별로 항목을 add/refresh하며,
        // 서로 다른 소스는 합산 중첩된다(같은 sourceId는 갱신만). 배율(예: 1.2)은 보너스(0.2)로 저장해
        // 실효 배율 = 1 + 보너스 합. duration>0이면 그 시간 후 만료(Update에서 정리), duration<=0이면
        // 지속형(만료 없음 → RemoveBuff로만 해제). 이벤트형 버프 타워는 지속형, 스킬은 시간제로 쓴다.
        public void ApplyBuff(int sourceId, float damageMul, float attackSpeedMul, float duration)
        {
            activeBuffs[sourceId] = new BuffEntry
            {
                DamageBonus = damageMul - 1f,
                SpeedBonus = attackSpeedMul - 1f,
                Expiry = duration > 0f ? Time.time + duration : float.PositiveInfinity,
            };
            RecomputeBuffs();
        }

        // 특정 소스의 버프를 즉시 제거(만료 대기 없이). 이벤트형 버프 타워가 밤 종료·비활성화 시 호출한다.
        public void RemoveBuff(int sourceId)
        {
            if (activeBuffs.Remove(sourceId)) RecomputeBuffs();
        }

        // 만료된 버프를 제거한다(매 프레임, 페이즈 무관). 활성 항목 수가 적어 비용은 무시할 수준.
        void PruneExpiredBuffs()
        {
            if (activeBuffs.Count == 0) return;

            expiredScratch.Clear();
            foreach (var kv in activeBuffs)
                if (Time.time >= kv.Value.Expiry) expiredScratch.Add(kv.Key);

            if (expiredScratch.Count == 0) return;
            foreach (var key in expiredScratch) activeBuffs.Remove(key);
            RecomputeBuffs();
        }

        // 실효 배율 = 1 + 모든 활성 소스 보너스의 합(합산 중첩).
        void RecomputeBuffs()
        {
            float dmg = 0f, spd = 0f;
            foreach (var b in activeBuffs.Values)
            {
                dmg += b.DamageBonus;
                spd += b.SpeedBonus;
            }
            damageMultiplier = 1f + dmg;
            attackSpeedMultiplier = 1f + spd;
        }

        void Update()
        {
            PruneExpiredBuffs();

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