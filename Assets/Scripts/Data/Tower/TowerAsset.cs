using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;   // ImpactKind / FlightMode — 투사체가 "어떻게 날아가서 어떻게 터지는가"

[CreateAssetMenu(fileName = "TowerAsset", menuName = "Scriptable Objects/TowerAsset")]
public class TowerAsset : ScriptableObject
{
    public string TowerID;
    public GameObject TowerPrefab;
    public GameObject GhostPrefab; // 배치 전 미리보기용 투명 타워 프리팹. TowerPrefab과 동일한 구조를 가져야 한다.

    [HideInInspector]
    public TowerData Data;

    public List<ResourceCost> Cost;

    // ── 평탄 스키마 (#274 Phase 1) ──────────────────────────────────────────────
    // 타입별 래퍼(Single/Area/Chain/Magic)를 풀어 한 층으로 편다. 타입별 필드가 7개뿐이라
    // 그룹 클래스 없이도 읽을 만하고, 안 쓰는 타워에서 값이 0이어도 아무도 안 읽으므로 무해하다.
    // 자세한 근거: Docs/Core/TowerRedesign.md §6
    [Header("공격")]
    public AttackFields Attack;

    [Header("명중")]
    public ImpactKind Impact;
    public float SplashRadius;          // Impact = Area
    public float ChainRadius;           // Impact = Chain
    public int MaxChainTargets;         // Impact = Chain — 최초 대상 포함 총 타격 수
    public float ChainDamageFalloff;    // Impact = Chain — 홉마다 곱해지는 계수

    [Header("오라")]
    public BuffAuraFields BuffAura;
    public DebuffAuraFields DebuffAura;

    // 다수 타겟 동시 잠금 + 균일 지속딜(#298). 투사체가 없는 행동 축이라 Attack과 완전히 분리된
    // 자기 필드 블록을 쓴다 — Attack.Flight/ProjectilePrefab 같은 투사체 전용 개념이 여기 섞이지 않는다.
    [Header("빔(다중 타겟 지속딜)")]
    public BeamFields Beam;

    // 이 타워가 대상에게 거는 효과와 그 수치(#274 Phase 4). 인스펙터에서 `+ Burn`, `+ Slow`를 골라 붙인다.
    //
    // **공격 액션과 디버프 오라가 같은 리스트를 공유한다** — "맞으면 화상"과 "장판에 화상"은 거는 방식만
    // 다르고 효과 자체는 같기 때문이다. 덕분에 화상 장판 타워가 새 코드 없이 만들어진다.
    //
    // ⚠ 리스트 **순서를 섞거나 항목을 지우지 말 것.** 소스 키가 `Kind`로 채번되므로 종류를 바꾸면
    // 진행 중이던 효과가 대상 쪽에 회수되지 않는 유령으로 남는다.
    [Header("명중 효과")]
    [SerializeReference] public List<NorthLand.Combat.HitEffect> Effects = new();

    /// 이 타워가 해당 액션을 갖는지 — **프리팹의 `Tower.Actions`가 정본이다**(#274).
    ///
    /// 배치 **전** 경로(툴팁·저작 검증)는 인스턴스가 없어 `Tower.Has&lt;T&gt;()`를 직접 못 부른다.
    /// 프리팹의 직렬화된 액션 리스트를 그대로 들여다보므로 초기화 없이도 답할 수 있다.
    public bool HasAction<T>() where T : NorthLand.Combat.TowerAction
        => TowerPrefab != null
           && TowerPrefab.TryGetComponent(out NorthLand.Combat.Tower tower)
           && tower.Has<T>();

    /// 배치 프리뷰에 그릴 반경. 공격 사거리와 오라 반경 중 **큰 쪽** — 플레이어가 알아야 할 영향 범위다.
    ///
    /// WL-056의 "오라 반경 단일 출처" 성질을 유지하면서 구 `TowerType` 분기만 걷어낸 것이다.
    /// 구 `MagicRadius`는 `MagicEffectType`으로 Buff/Debuff를 골랐는데, 오라를 둘 다 가진 타워를
    /// 표현할 수 없었다. 최댓값을 쓰면 공격+오라 하이브리드 타워도 자연히 커버된다.
    public float PreviewRadius => Mathf.Max(
        Mathf.Max(
            Attack != null ? Attack.AttackRange : 0f,
            Beam != null ? Beam.Range : 0f),
        Mathf.Max(
            BuffAura != null ? BuffAura.Radius : 0f,
            DebuffAura != null ? DebuffAura.Radius : 0f));

#if UNITY_EDITOR
    // 저작 실수를 **저장하는 순간** 드러낸다(WL-130 해소).
    //
    // 종류의 정본이 프리팹의 Actions로 옮겨가면서(#274) SO 수치와 프리팹 구성이 갈라질 수 있게 됐다 —
    // "AttackAction은 붙였는데 공격 수치가 0"이나 "Impact=Area인데 SplashRadius가 0" 같은 조합은
    // 배치해도 예외 없이 그냥 아무 일도 안 일어나서, 예전엔 플레이해보기 전엔 알 수 없었다.
    // (WL-001의 lightning_tower 전 필드 0이 정확히 그 무증상 패턴이다.)
    void OnValidate()
    {
        // 프리팹이 없으면 액션↔수치 짝 검사는 못 한다. 다만 **무조건 조용히 넘어가면 안 된다** —
        // 그러면 이 훅이 가장 잡아야 할 상황(프리팹 Missing → Actions 빔 → 무증상 무동작)에서만
        // 침묵한다. 수치를 이미 적어둔 SO는 "붙이는 걸 잊었다"에 훨씬 가깝다(PR #278 리뷰).
        if (TowerPrefab == null)
        {
            bool authored = (Attack != null && (Attack.AttackDamage > 0f || Attack.ProjectilePrefab != null))
                            || (BuffAura != null && BuffAura.Radius > 0f)
                            || (DebuffAura != null && DebuffAura.Radius > 0f)
                            || (Beam != null && Beam.DamagePerTick > 0f);

            if (authored)
            {
                Debug.LogWarning(
                    $"[TowerAsset] {name}: 수치는 저작돼 있는데 TowerPrefab이 비었습니다 — 배치가 거부되거나 " +
                    "(프리팹 참조가 깨진 경우) 아무 동작도 하지 않습니다. `Assets/Imported/` 동기화도 확인하세요.", this);
            }
            return;   // 수치도 없으면 아직 저작 중 — 조용히 넘어간다
        }

        if (!TowerPrefab.TryGetComponent(out NorthLand.Combat.Tower tower))
        {
            Debug.LogWarning($"[TowerAsset] {name}: TowerPrefab '{TowerPrefab.name}'에 Tower 컴포넌트가 없습니다.", this);
            return;
        }

        var actions = tower.Actions;
        bool hasAttack = false, hasBuff = false, hasDebuff = false, hasBeam = false;
        var seen = new HashSet<System.Type>();

        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            if (a == null)
            {
                // [SerializeReference]는 클래스 rename·삭제 시 항목을 null로 남긴다.
                Debug.LogWarning($"[TowerAsset] {name}: 프리팹 Actions[{i}]가 비었습니다 — " +
                                 "클래스 이름이 바뀌었는지 확인하세요([MovedFrom] 필요).", this);
                continue;
            }

            // 같은 타입이 둘이면 TowerAction.SourceId가 충돌해 스탯·상태이상 슬롯을 서로 덮어쓴다.
            if (!seen.Add(a.GetType()))
                Debug.LogWarning($"[TowerAsset] {name}: 프리팹에 {a.GetType().Name}이(가) 둘 이상입니다 — " +
                                 "소스 키가 충돌합니다.", this);

            hasAttack |= a is NorthLand.Combat.AttackAction;
            hasBuff |= a is NorthLand.Combat.BuffAuraAction;
            hasDebuff |= a is NorthLand.Combat.DebuffAuraAction;
            hasBeam |= a is NorthLand.Combat.BeamAction;
        }

        // ── 액션 ↔ 수치 짝 검사 ────────────────────────────────────────────
        bool attackAuthored = Attack != null && (Attack.AttackDamage > 0f || Attack.ProjectilePrefab != null);
        if (hasAttack && !attackAuthored)
            Debug.LogWarning($"[TowerAsset] {name}: 프리팹에 AttackAction이 있는데 공격 수치가 비었습니다 " +
                             "(Damage 0 + ProjectilePrefab 없음) — 배치해도 아무것도 쏘지 않습니다.", this);
        if (!hasAttack && attackAuthored)
            Debug.LogWarning($"[TowerAsset] {name}: 공격 수치를 적었는데 프리팹에 AttackAction이 없습니다 " +
                             "— 이 수치는 아무도 읽지 않습니다.", this);

        // 비행 부품 누락(#274 Phase 4.5). 이것도 **예외 없이 조용히 안 쏘는** 조합이라 여기서 잡는다 —
        // [SerializeReference]는 클래스 rename·삭제 시 항목을 null로 남기므로 저작 실수만의 문제가 아니다.
        if (hasAttack && attackAuthored && Attack.Flight == null)
            Debug.LogWarning($"[TowerAsset] {name}: 공격 수치는 있는데 Attack.Flight(비행 방식)가 비었습니다 " +
                             "— 투사체가 생성되지 않습니다. Homing 또는 Ballistic을 지정하세요.", this);

        if (hasBuff && (BuffAura == null || BuffAura.Radius <= 0f))
            Debug.LogWarning($"[TowerAsset] {name}: BuffAuraAction이 있는데 BuffAura.Radius가 0입니다.", this);
        if (hasDebuff && (DebuffAura == null || DebuffAura.Radius <= 0f))
            Debug.LogWarning($"[TowerAsset] {name}: DebuffAuraAction이 있는데 DebuffAura.Radius가 0입니다.", this);

        // ── 명중 방식 ↔ 그 방식이 요구하는 수치 ─────────────────────────────
        if (hasAttack && Impact == NorthLand.Combat.ImpactKind.Area && SplashRadius <= 0f)
            Debug.LogWarning($"[TowerAsset] {name}: Impact=Area인데 SplashRadius가 0입니다 — 단일 명중과 같아집니다.", this);
        if (hasAttack && Impact == NorthLand.Combat.ImpactKind.Chain && MaxChainTargets <= 1)
            Debug.LogWarning($"[TowerAsset] {name}: Impact=Chain인데 MaxChainTargets가 {MaxChainTargets}입니다 — 튕기지 않습니다.", this);

        // ── 산탄(PelletCount>1) 저작 규칙(#298) ─────────────────────────────
        // 셋 다 "예외 없이 조용히 의미가 사라지는" 조합이라 여기서 잡는다.
        if (hasAttack && attackAuthored && Attack.PelletCount > 1)
        {
            if (Attack.Flight != null && !(Attack.Flight is NorthLand.Combat.StraightFlight))
                Debug.LogWarning($"[TowerAsset] {name}: PelletCount>1인데 Flight가 StraightFlight가 아닙니다 — " +
                                 "Homing/Ballistic과 조합하면 펠릿 전부가 같은 대상으로 수렴해 부채꼴이 사라집니다.", this);

            if (Impact != NorthLand.Combat.ImpactKind.Area)
                Debug.LogWarning($"[TowerAsset] {name}: PelletCount>1인데 Impact=Area가 아닙니다 — " +
                                 "Single은 위치와 무관하게 명중 처리돼 빗나간 펠릿도 피해를 줍니다.", this);

            // AttackAction.TryAttack의 각도 분할이 Mathf.Lerp(-half, +half, ...)이므로 SpreadAngle이 0이면
            // 모든 펠릿의 각도가 0으로 접힌다 — 위 둘과 같은 "예외 없이 조용히 의미가 사라지는" 조합이다.
            if (Attack.SpreadAngle <= 0f)
                Debug.LogWarning($"[TowerAsset] {name}: PelletCount({Attack.PelletCount})>1인데 " +
                                 $"SpreadAngle이 {Attack.SpreadAngle}입니다 — 펠릿이 전부 같은 방향으로 겹쳐 날아가 " +
                                 $"부채꼴이 사라지고, 한 대상에 피해가 {Attack.PelletCount}배로 집중됩니다.", this);
        }

        // 부메랑도 산탄과 같은 이유로 Area가 필요하다(#298) — Single은 위치 무관 판정이라
        // 왕복 경로의 재타격 게이트와 무관하게 항상 원래 타겟만 맞아버린다.
        if (hasAttack && attackAuthored && Attack.Flight is NorthLand.Combat.BoomerangFlight boomerang)
        {
            if (Impact != NorthLand.Combat.ImpactKind.Area)
                Debug.LogWarning($"[TowerAsset] {name}: Flight=BoomerangFlight인데 Impact=Area가 아닙니다 — " +
                                 "Single/Chain은 위치 기반 재타격을 지원하지 않습니다.", this);

            // BoomerangFlight는 HitRadius로 "닿았다"만 알리고, 실제로 누구를 때릴지는
            // Projectile.ApplyArea가 SplashRadius로 조회해 정한다 — SplashRadius가 HitRadius보다
            // 좁으면 접촉을 알린 프레임에 아무도 안 맞을 수 있고, 그 적이 원장에 오르지 않으므로
            // 접촉이 유지되는 내내 매 프레임 헛된 Impact가 반복된다.
            if (Impact == NorthLand.Combat.ImpactKind.Area && SplashRadius < boomerang.HitRadius)
                Debug.LogWarning($"[TowerAsset] {name}: SplashRadius({SplashRadius})가 BoomerangFlight의 " +
                                 $"HitRadius({boomerang.HitRadius})보다 좁습니다 — 접촉 판정된 적 일부가 " +
                                 "실제 데미지 적용 범위에서 빠질 수 있습니다. SplashRadius ≥ HitRadius로 맞추세요.", this);
        }

        // ── 빔(BeamAction) ↔ 수치 짝 검사(#298) ─────────────────────────────
        bool beamAuthored = Beam != null && Beam.DamagePerTick > 0f;
        if (hasBeam && !beamAuthored)
            Debug.LogWarning($"[TowerAsset] {name}: 프리팹에 BeamAction이 있는데 Beam 수치가 비었습니다 " +
                             "(DamagePerTick 0) — 배치해도 피해가 안 나갑니다.", this);
        if (!hasBeam && beamAuthored)
            Debug.LogWarning($"[TowerAsset] {name}: Beam 수치를 적었는데 프리팹에 BeamAction이 없습니다 " +
                             "— 이 수치는 아무도 읽지 않습니다.", this);
        if (hasBeam && beamAuthored && Beam.MaxTargets <= 0)
            Debug.LogWarning($"[TowerAsset] {name}: BeamAction이 있는데 Beam.MaxTargets가 {Beam.MaxTargets}입니다 " +
                             "— 아무도 안 잠급니다.", this);
        if (hasBeam && beamAuthored && Beam.TickInterval <= 0f)
            Debug.LogWarning($"[TowerAsset] {name}: BeamAction이 있는데 Beam.TickInterval이 0 이하입니다 " +
                             "— 매 프레임 재잠금·재적용이 돌아 비용이 폭주할 수 있습니다.", this);
    }
#endif

    // Single/Area/Chain 공통 공격 스탯. Combat/TowerData.cs(SUNGSOO)의 필드와 의미 대응되도록
    // 맞춰서, 추후 Combat이 이 파이프라인으로 옮겨올 때 매핑이 쉽도록 한다.
    [System.Serializable]
    public class AttackFields
    {
        public float AttackDamage;
        public float AttackRange;
        public float AttackInterval;
        public GameObject ProjectilePrefab;   // 겉모습만 고른다 — 비행·명중은 아래 값들이 정한다

        // ── 비행 (#274 Phase 1에서 탄환 프리팹 → 여기로 이관, Phase 4.5에서 부품화) ──────
        // 궤적은 착탄 시간 → 움직이는 적에 대한 실효 DPS를 정하므로 **비주얼이 아니라 밸런스**다.
        // 탄환 프리팹에 남는 것은 rotationOffset(모델 축 보정)뿐이다. 근거: TowerRedesign.md §6.1
        //
        // 구 `FlightMode` enum + `ProjectileSpeed`/`ArcHeight` 3필드를 부품 하나로 대체했다 —
        // 인스펙터에서 `Homing`을 고르면 그 자리에 그 종류의 수치가 함께 뜬다(HitEffect와 같은 패턴).
        // 부메랑의 왕복 거리처럼 **특정 비행 방식에만 있는 수치**가 자기 부품 안에 들어가므로
        // 이 클래스가 다시 부풀지 않는다. 새 비행 방식 = ProjectileFlight 파생 1개(Projectile 무수정).
        [SerializeReference] public NorthLand.Combat.ProjectileFlight Flight;

        // ── 산탄 (#298) ─────────────────────────────────────────────────────
        // 기본값 1 = 발사당 1발, 기존 전 타워 거동 무변경(회귀 위험 없음). 2 이상이면
        // `AttackAction.TryAttack`이 SpreadAngle 안에 균등 분할한 각도로 여러 발을 동시 Instantiate한다.
        // 데미지(AttackDamage)는 펠릿 1발당 값이다 — 전탄 명중 시 N배(근접 밀집 화력 보상).
        [Tooltip("발사당 펠릿 수. 1이면 기존 거동과 동일.")]
        public int PelletCount = 1;

        [Tooltip("PelletCount>1일 때 펠릿들이 균등 분할되는 부채꼴 총 각도(도).")]
        public float SpreadAngle;
    }

    [System.Serializable]
    public class BuffAuraFields
    {
        // 버프는 이벤트형(배치 즉시 부여 + 타워 추가/제거 시 재적용, 범위 유지형)이라 Interval/Duration이 불필요(#164). Radius+Modifiers만 사용한다.
        public float Radius;
        public List<StatModifier> Modifiers;
        public OptionalDamage Damage;   // (현재 미사용) 향후 아군 힐 등 확장 여지로 보존
    }

    [System.Serializable]
    public class DebuffAuraFields
    {
        public float Radius;

        // 재스캔 주기. 무엇을 거는지는 TowerAsset.Effects가 정한다(#274 Phase 4) —
        // 예전에는 여기에 Duration/Modifiers/Damage가 수기 필드로 박혀 있어 공격 명중 효과와 저작이 갈렸다.
        public float Interval;
    }

    // 인페르노 타워(#298) — 다수 타겟 동시 잠금 + 균일 지속딜. 램프업(유지 시간에 따른 가중치)은
    // 이 이슈 범위 밖이라 필드가 없다 — 별도 "램프업 타워" 이슈에서 공용 부품으로 얹을 예정.
    [System.Serializable]
    public class BeamFields
    {
        public float Range;

        [Tooltip("한 번에 동시 잠그는 최대 대상 수.")]
        public int MaxTargets = 1;

        [Tooltip("잠금·피해 적용 주기(초). AttackSpeed 원장을 거쳐 실제 틱 간격이 줄어든다.")]
        public float TickInterval = 1f;

        [Tooltip("틱마다 각 대상에게 들어가는 피해(대상별 동일, 유지 시간 가중치 없음).")]
        public float DamagePerTick;
    }
}

public enum ModifiableStat
{
    AttackDamage,
    AttackSpeed,
    MoveSpeed,
    Armor,
}

[System.Serializable]
public class StatModifier
{
    public ModifiableStat Stat;
    public float Amount;
    public bool IsPercentage;
}

[System.Serializable]
public class OptionalDamage
{
    public bool HasDamage;
    public float DamageAmount;
    public float TickInterval;
}
