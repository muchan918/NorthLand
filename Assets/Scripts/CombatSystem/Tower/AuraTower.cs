using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using NorthLand.Combat;
using CombatSpace;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NorthLand.Combat
{
    // 마법 타워 공통 컴포넌트. TowerAsset(Magic)을 참조하고 MagicEffectType으로 Buff/Debuff 분기한다:
    //   Debuff → 적군 대상, DoT. 적이 움직이므로 UniTask 폴링 루프(Interval)로 재스캔·갱신(StatusEffectHandler 소유).
    //   Buff   → 아군 타워 대상, 스탯 버프. 배치 즉시(낮 포함) 사거리 내 타워에 부여하고, 타워 추가/제거 시
    //            (Tower.ActiveChanged) 재적용한다 — 폴링 없이 낮 정보 패널에도 버프 스탯이 즉시 반영(#164). Interval/Duration 미사용.
    // DamageInfo의 데미지 소스로 쓰이기 위해 IAttacker를 구현한다.
    public class AuraTower : MonoBehaviour, IAttacker, ISelectable
    {
        [SerializeField] TowerAsset data;

        // 배치된 타워의 원본 SO 읽기 접근자(Tower.Asset과 대칭). 배치 후 프리팹↔SO 정합 검증에 쓴다(WL-106).
        public TowerAsset Asset => data;

        // 디버프=적 레이어 / 버프=아군 레이어. 프리팹에서 지정.
        [SerializeField] LayerMask targetLayerMask;

        [Header("Debug")]
        [SerializeField] bool debugLog = false;

        // 선택 시 오라 반경 원 색(배치 미리보기와 구분되도록 초록 계열, #192). 채움은 반투명.
        [Header("선택 사거리 표시")]
        [SerializeField] Color selectionRangeColor = new Color(0.3f, 1f, 0.4f, 0.9f);
        [SerializeField] Color selectionRangeFillColor = new Color(0.3f, 1f, 0.4f, 0.15f);
        RangeCircle _rangeCircle; // lazy 생성, 자식 GO

        readonly Collider[] hitBuffer = new Collider[32];
        CancellationTokenSource cts;
        int effectId;

        public Faction Faction => Faction.Player;

        bool IsMagic => data != null && data.TowerType == TowerType.Magic;
        bool IsDebuff => IsMagic && data.MagicEffectType == MagicEffectType.Debuff;
        bool IsBuff => IsMagic && data.MagicEffectType == MagicEffectType.Buff;

        // 현재 활성 오라 데이터 (null-safe). data.Magic 미할당 시에도 NRE 없이 null.
        TowerAsset.DebuffAuraFields DebuffAura => IsDebuff ? data.Magic?.DebuffAura : null;
        TowerAsset.BuffAuraFields BuffAura => IsBuff ? data.Magic?.BuffAura : null;

        // 반경은 TowerAsset.MagicRadius 단일 출처를 사용(WL-056) — TowerPlacer 프리뷰와 규칙 공유.
        float Radius
        {
            get
            {
                if (data == null)
                {
                    return 0f;
                }

                float baseRadius =
                    data.MagicRadius;

                if (tileBuff == null)
                {
                    return baseRadius;
                }

                float flat =
                    tileBuff.GetValue(
                        TileBuffStat.AttackRange,
                        TileModifierMode.Flat);

                float percentage =
                    tileBuff.GetValue(
                        TileBuffStat.AttackRange,
                        TileModifierMode.Percentage);

                return
                    (baseRadius + flat) *
                    (1f + percentage / 100f);
            }
        }

        float Interval => DebuffAura?.Interval ?? 0f;   // 버프는 이벤트형이라 Interval 미사용 — 디버프 폴링 주기만.

        // IAttacker 계약(공개 스탯). 오라 타워라 값이 없으면 0 가드.
        public float AttackDamage => DebuffAura?.Damage != null ? DebuffAura.Damage.DamageAmount : 0f;
        public float AttackRange => Radius;
        public float AttackInterval => Interval;


        private TowerTileBuff tileBuff;

        // 오라 타워는 단일 대상 즉시 공격 경로를 쓰지 않는다. 오라 루프로만 동작.
        public bool TryAttack(IDamageable target) => false;

        // 정보 패널 연동(#153/#164) — Tower와 동일 패턴. 마법 타워도 선택 시 자기 정보를 연다.
        // (MouseManager는 hit.collider.TryGetComponent<ISelectable>로 판정하므로 콜라이더와 같은 GO에 있어야 함)
        public void OnSelected()
        {
            if (data == null)
            {
                Debug.LogError("[AuraTower] TowerAsset 미할당", this);
                return;
            }

            data.Data ??= DataTableManager.Get<TowerTable>("TowerTable").Get(data.TowerID);
            if (data.Data == null)
            {
                Debug.LogError($"[AuraTower] TowerData 없음 (TowerID={data.TowerID})", this);
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

        // 선택 시 오라 반경 원(버프 반영 Radius)을 표시한다. 원은 자식 GO라 타워 파괴 시 함께 정리된다.
        void ShowRangeCircle()
        {
            if (_rangeCircle == null)
                _rangeCircle = RangeCircle.Create(transform, selectionRangeFillColor, selectionRangeColor, "TowerRangeSelection");

            _rangeCircle.SetRadius(Radius);
            _rangeCircle.Show();
        }

        // 마법 타워 정보 텍스트: 오라 반경 + 효과 요약(디버프 DoT / 버프 스탯 증가). 라벨은 game.tower.* 로컬라이즈 키.
        string BuildStatsText()
        {
            string rangeLabel = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, "game.tower.attack_range");
            string text = $"{rangeLabel}: {Radius:0.#}";

            if (IsDebuff)
            {
                var dot = DebuffAura?.Damage;
                if (dot != null && dot.HasDamage)
                    text += $"\nDoT: {dot.DamageAmount:0.#} / {dot.TickInterval:0.#}s";

                float slowMul = ComputeSlowMultiplier(DebuffAura?.Modifiers);
                if (slowMul < 1f)
                    text += $"\nSlow: -{(1f - slowMul) * 100f:0.#}%";
            }
            else if (IsBuff && BuffAura?.Modifiers != null)
            {
                foreach (var mod in BuffAura.Modifiers)
                {
                    if (mod == null) continue;
                    string sign = mod.Amount >= 0 ? "+" : "";
                    text += $"\n{mod.Stat} {sign}{mod.Amount:0.#}{(mod.IsPercentage ? "%" : "")}";
                }
            }
            return text;
        }


        void OnEnable()
        {
            // Tower.OnEnable의 Magic 오부착 가드와 대칭(WL-106). AuraTower는 Magic 타워 전용이다.
            // Unity가 중첩 Serializable(Magic)을 항상 인스턴스화하므로 data.Magic null 검사는 무의미 —
            // 실제 무증상 오작성은 (a) MagicEffectType 미선택(None) (b) 활성 오라 블록이 비어 있음이며, 아래에서 드러낸다.
            if (!IsMagic)
            {
                Debug.LogError("[AuraTower] Magic TowerAsset이 아님(미할당 또는 비-Magic) — Magic 타워 전용 컴포넌트입니다", this);
                return;
            }
            if (IsDebuff)
            {
                var d = DebuffAura;
                bool hasDot = d != null && d.Damage != null && d.Damage.HasDamage;
                bool hasSlow = d != null && ComputeSlowMultiplier(d.Modifiers) < 1f;
                if (d == null || d.Radius <= 0f || (!hasDot && !hasSlow))
                {
                    Debug.LogError("[AuraTower] Debuff 오라 내용 없음(반경 0 또는 DoT·슬로우 모두 없음) — 아무 효과도 적용되지 않습니다", this);
                    return;
                }
            }
            else if (IsBuff)
            {
                var b = BuffAura;
                if (b == null || b.Modifiers == null || b.Modifiers.Count == 0)
                {
                    Debug.LogError("[AuraTower] Buff 오라 Modifiers 없음 — 아무 효과도 적용되지 않습니다", this);
                    return;
                }
            }
            else
            {
                Debug.LogError("[AuraTower] MagicEffectType이 None — Buff/Debuff를 선택해야 합니다", this);
                return;
            }

            // 같은 TowerID끼리 하나의 DoT 효과를 공유·갱신(디버프 전용 키). 버프는 GetInstanceID()를 소스키로 쓴다.
            effectId = !string.IsNullOrEmpty(data.TowerID) ? data.TowerID.GetHashCode() : GetInstanceID();

            if (IsDebuff)
            {
                // 디버프(DoT): 적이 계속 움직이므로 Interval마다 재스캔하는 폴링 루프가 필수.
                cts = new CancellationTokenSource();
                AuraLoop(cts.Token).Forget();
            }
            else if (IsBuff)
            {
                // 버프: 배치 즉시(낮 포함) 사거리 내 타워에 부여 → 낮 정보 패널에도 버프된 스탯이 보인다.
                // 이후 타워가 추가/제거될 때마다(Tower.ActiveChanged) 재적용해 새로 지은 타워도 반영. 폴링은 없다(#164).
                Tower.ActiveChanged += ApplyBuffToTowersInRange;
                ApplyBuffToTowersInRange();
            }
        }
        private void Start()
        {
            tileBuff =
                GetComponent<TowerTileBuff>();

            // OnEnable은 TowerTileBuff가 추가되기 전에 실행되므로
            // 배치가 완료된 후 버프 오라 범위를 다시 계산한다.
            if (IsBuff)
            {
                ApplyBuffToTowersInRange();
            }
        }

        void OnDisable()
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = null;

            // 버프 이벤트 구독 해제 + 이 타워가 부여한 버프를 대상에서 제거(철거 시).
            Tower.ActiveChanged -= ApplyBuffToTowersInRange;
            if (IsBuff) RemoveBuffFromAll();
        }

        async UniTaskVoid AuraLoop(CancellationToken ct)
        {
            float loopInterval = Mathf.Max(Interval, 0.05f);   // 0 이하 폭주 방지

            while (!ct.IsCancellationRequested)
            {
                Tick();

                bool canceled = await UniTask
                    .Delay(TimeSpan.FromSeconds(loopInterval), cancellationToken: ct)
                    .SuppressCancellationThrow();
                if (canceled) return;
            }
        }

        void Tick()
        {
            // 낮/밤 게이팅: 밤 페이즈에만 동작 (WL-019). 버프는 이벤트 구동이라 이 루프는 디버프만 처리한다.
            if (DayNightManager.Instance != null &&
                DayNightManager.Instance.CurrentPhase != DayNightManager.Phase.Night) return;

            var debuff = DebuffAura;
            if (debuff != null) ApplyDebuff(debuff);
        }

        // 사거리 내 적군에게 디버프(DoT + 슬로우)를 갱신. 상태 소유는 대상의 StatusEffectHandler.
        // 슬로우는 DebuffAura.Modifiers의 MoveSpeed 항목을 배율로 환산해 적용한다(WL-076).
        void ApplyDebuff(TowerAsset.DebuffAuraFields aura)
        {
            if (aura == null) return;

            var dot = aura.Damage;
            bool hasDot = dot != null && dot.HasDamage;

            float slowMultiplier = ComputeSlowMultiplier(aura.Modifiers);
            bool hasSlow = slowMultiplier < 1f;

            if (!hasDot && !hasSlow) return;   // 적용할 효과 없음

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, Radius, hitBuffer, targetLayerMask);

            for (int i = 0; i < count; i++)
            {
                var dmg = hitBuffer[i].GetComponentInParent<IDamageable>();
                if (dmg == null || dmg.IsDead) continue;
                if (dmg.Faction == Faction) continue;    // 디버프는 아군 제외 (적군만)

                if (dmg is not Component target) continue;

                var handler = target.GetComponent<StatusEffectHandler>();
                if (handler == null) handler = target.gameObject.AddComponent<StatusEffectHandler>();

                handler.debugLog = debugLog;
                if (hasDot) handler.ApplyOrRefresh(effectId, dot.DamageAmount, dot.TickInterval, aura.Duration, this);
                if (hasSlow) handler.ApplySlow(effectId, slowMultiplier, aura.Duration);
            }
        }

        // MoveSpeed StatModifier들을 곱연산 슬로우 배율로 환산한다. 버프(ApplyBuffToTowersInRange)와 동일 규칙:
        // IsPercentage=true면 Amount를 %로(예: -40 → x0.6), false면 배율 가산분으로(예: -0.4 → x0.6).
        // 감속만 의미가 있으므로 [0,1]로 제한한다(양수 Amount로 적을 가속시키지 않음). 결과 1 = 감속 없음.
        static float ComputeSlowMultiplier(System.Collections.Generic.List<StatModifier> modifiers)
        {
            float mul = 1f;
            if (modifiers != null)
            {
                foreach (var mod in modifiers)
                {
                    if (mod == null || mod.Stat != ModifiableStat.MoveSpeed) continue;
                    mul *= mod.IsPercentage ? 1f + mod.Amount / 100f : 1f + mod.Amount;
                }
            }
            return Mathf.Clamp01(mul);
        }

        // 배치 시 + 타워 추가/제거 시(Tower.ActiveChanged) 호출 — 사거리 내 아군 타워를 라이브 스캔해 지속형 버프를 부여한다.
        // 이미 버프된 타워는 같은 소스키(GetInstanceID)로 갱신만 되므로 반복 호출해도 중복 누적되지 않는다.
        void ApplyBuffToTowersInRange()
        {
            var aura = BuffAura;
            if (aura == null) return;

            // Modifiers를 Tower.ApplyBuff가 받는 배율로 환산한다. Tower.ApplyBuff는 배율만 지원하므로
            // 아군 타워에 의미 있는 AttackDamage/AttackSpeed만 반영한다(MoveSpeed/Armor는 적 스탯).
            // IsPercentage=true면 Amount를 퍼센트로(예: 25 → x1.25), false면 배율 가산분으로(예: 0.25 → x1.25) 해석.
            float damageMul = 1f;
            float attackSpeedMul = 1f;
            if (aura.Modifiers != null)
            {
                foreach (var mod in aura.Modifiers)
                {
                    if (mod == null) continue;
                    float mul = mod.IsPercentage ? 1f + mod.Amount / 100f : 1f + mod.Amount;
                    if (mod.Stat == ModifiableStat.AttackDamage) damageMul *= mul;
                    else if (mod.Stat == ModifiableStat.AttackSpeed) attackSpeedMul *= mul;
                }
            }

            // 실제 강화가 없으면(배율이 1) 순회·적용을 건너뛴다.
            if (Mathf.Approximately(damageMul, 1f) && Mathf.Approximately(attackSpeedMul, 1f)) return;

            // 물리 OverlapSphere 대신 Tower.Active(전 타워 자가 등록 리스트)를 거리 필터링한다.
            // 전용 Tower 레이어가 없어 레이어마스크 방식이 취약하고, 버프 대상은 AttackFields를 가진
            // Tower(Single/Area/Chain)뿐이라 이 리스트가 정확한 대상 집합이다(AuraTower는 미포함 → 자기 자신도 제외).
            float sqrRadius = Radius * Radius;
            var towers = Tower.Active;
            for (int i = 0; i < towers.Count; i++)
            {
                var tower = towers[i];
                if (tower == null) continue;
                if (tower.Faction != Faction) continue;   // 버프는 아군만
                if ((tower.transform.position - transform.position).sqrMagnitude > sqrRadius) continue;

                // duration<=0 → 지속형(이 버프 타워 철거/비활성 시 RemoveBuffFromAll로 해제 — 밤 게이팅 없음).
                // 소스키=GetInstanceID()라 같은 종류 버프 타워도 각각 별개 소스로 합산 중첩되고, 스킬 등 다른 소스와도 합산된다.
                tower.ApplyBuff(GetInstanceID(), damageMul, attackSpeedMul, 0f);
            }
        }

        // 이 버프 타워 비활성화(철거) 시 — 이 버프 타워가 부여한 버프를 모든 타워에서 제거.
        void RemoveBuffFromAll()
        {
            int id = GetInstanceID();
            var towers = Tower.Active;
            for (int i = 0; i < towers.Count; i++)
                if (towers[i] != null) towers[i].RemoveBuff(id);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!IsMagic) return;
            Handles.color = IsBuff ? new Color(0.3f, 0.7f, 1f) : new Color(0.6f, 0.2f, 0.85f);
            Handles.DrawWireDisc(transform.position, Vector3.up, Radius);
        }
#endif
    }
}
