using System;
using System.Collections.Generic;
using UnityEngine;
using CombatSpace;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NorthLand.Combat
{
    public class Tower : MonoBehaviour, IAttacker, ISelectable
    {
        [SerializeField] TowerAsset data;

        // 배치된 타워의 원본 SO 읽기 접근자. 합성(#195)에서 재료 타워의 TowerID/스탯을 조회하는 데 쓴다.
        public TowerAsset Asset => data;

        // TODO(TBD): 대상 탐지 필터링을 LayerMask로 할지 Tag로 할지 미확정. 임시 LayerMask.
        [SerializeField] LayerMask enemyLayerMask;

        // 선택 시 사거리 원 색(배치 미리보기와 구분되도록 초록 계열, #192). 채움은 반투명.
        [Header("선택 사거리 표시")]
        [SerializeField] Color selectionRangeColor = new Color(0.3f, 1f, 0.4f, 0.9f);
        [SerializeField] Color selectionRangeFillColor = new Color(0.3f, 1f, 0.4f, 0.15f);
        RangeCircle _rangeCircle; // lazy 생성, 자식 GO — 선택 시 표시/해제 시 숨김

        // 투사체 생성 위치(포신/머즐). 미할당 시 기존처럼 타워 루트(바닥)에서 생성(하위 호환).
        [SerializeField] Transform firePoint;

        float cooldownTimer;
        readonly Collider[] hitBuffer = new Collider[16];

        // 소스별 버프를 합산 중첩한다(#164). 각 소스가 자기 항목을 add/refresh하고, 실효 배율 = 1 + 모든 소스 보너스의 합.
        // 같은 소스키는 갱신만 되어 자기 자신과는 중첩되지 않는다. 소스키 도메인(AuraTower/BuffSkillManager가 전달):
        //   버프 타워 = GetInstanceID()(인스턴스별) → 같은 종류 버프 타워도 서로 합산 중첩됨
        //   플레이어 버프 스킬 = 고정 문자열 해시 / 디버프(DoT) = TowerID 해시(같은 종류 공유·미중첩)
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


        private TowerTileBuff tileBuff;

        private void Start()
        {
            tileBuff = GetComponent<TowerTileBuff>();
        }

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
            ShowRangeCircle();
        }

        public void OnDeselected()
        {
            _rangeCircle?.Hide();
            TowerInfoUI.Instance.HideInfo();
        }

        // 선택 시 사거리 원(버프 반영 AttackRange)을 표시한다. 원은 자식 GO라 타워 파괴 시 함께 정리된다.
        void ShowRangeCircle()
        {
            if (_rangeCircle == null)
                _rangeCircle = RangeCircle.Create(transform, selectionRangeFillColor, selectionRangeColor, "TowerRangeSelection");

            _rangeCircle.SetRadius(AttackRange);
            _rangeCircle.Show();
        }

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
        public float AttackDamage
        {
            get
            {
                if (Attack == null)
                {
                    return 0f;
                }

                float baseDamage = Attack.AttackDamage;

                if (tileBuff == null)
                {
                    return baseDamage * damageMultiplier;
                }

                float flat = tileBuff.GetValue(
                    TileBuffStat.AttackDamage,
                    TileModifierMode.Flat);

                float percentage = tileBuff.GetValue(
                    TileBuffStat.AttackDamage,
                    TileModifierMode.Percentage);

                return (baseDamage + flat)
                    * (1f + percentage / 100f)
                    * damageMultiplier;
            }
        }

        //KSJ
        // 타일 버프가 적용된 최종 공격 사거리를 반환한다.
        // 고정 증가량을 먼저 더한 뒤 퍼센트 증가량을 곱한다.
        public float AttackRange
        {
            get
            {
                if (Attack == null)
                {
                    return 0f;
                }

                float baseRange =
                    Attack.AttackRange;

                // 버프 정보가 없으면 원래 사거리를 그대로 사용한다.
                if (tileBuff == null)
                {
                    return baseRange;
                }

                float flat =tileBuff.GetValue(TileBuffStat.AttackRange,TileModifierMode.Flat);

                float percentage =tileBuff.GetValue(TileBuffStat.AttackRange,TileModifierMode.Percentage);

                // 최종 사거리 = (기본 사거리 + 고정 증가량) × 퍼센트 배율
                return (baseRange + flat) *(1f + percentage / 100f);
            }
        }


        // 타일 버프와 기존 타워 버프를 반영한 최종 공격 간격.
        // 공격속도가 증가할수록 공격 간격은 짧아진다.
        public float AttackInterval
        {
            get
            {
                if (Attack == null)
                {
                    return 0f;
                }

                float percentage = tileBuff?.GetValue(
                    TileBuffStat.AttackSpeed,
                    TileModifierMode.Percentage) ?? 0f;

                float finalSpeedMultiplier =
                    attackSpeedMultiplier *
                    (1f + percentage / 100f);

                return Attack.AttackInterval /
                    Mathf.Max(finalSpeedMultiplier, 0.01f);
            }
        }

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

            // 타입별 명중 동작(단일/스플래시/체인)을 구성해 투사체에 전달 + 명중 시 스턴(있으면)
            var impact = BuildImpact();
            impact.StunDuration = atk.OnHitStunDuration;
            projectile.Init(target, AttackDamage, atk.ProjectileSpeed, this, impact);
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
            Handles.DrawWireDisc(transform.position,Vector3.up,AttackRange);
        }
#endif
    }
}