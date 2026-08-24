using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;   // ImpactKind / FlightMode — 투사체가 "어떻게 날아가서 어떻게 터지는가"

public enum TowerRarity
{
    Normal = 0,
    Rare = 1,
    Legendary = 2
}

[CreateAssetMenu(fileName = "TowerAsset", menuName = "Scriptable Objects/TowerAsset")]
public class TowerAsset : ScriptableObject
{
    public string TowerID;
    public GameObject TowerPrefab;
    public GameObject GhostPrefab; // 배치 전 미리보기용 투명 타워 프리팹. TowerPrefab과 동일한 구조를 가져야 한다.

    // 타워 선택 패널 버튼에 표시할 아이콘(ResourceAsset.Icon과 같은 규약 — 표기 소스를 SO 한 곳에 둔다).
    public Sprite Icon;
    // 배치 시 모델을 그리드 축에서 Y축으로 얼마나 돌릴지(도). **0 = 축 정렬**이 기본이고, 활대·포신이
    // 넓어 인접 배치 시 실루엣이 겹치는 모델만 값을 갖는다(아처 석궁 45°, #359).
    //
    // 왜 프리팹이 아니라 여기인가: `TowerPlacer`가 `Instantiate`의 세 번째 인자로 루트 회전을 통째로
    // 덮으므로 **프리팹 루트에 저작한 각도는 지워진다.** 모델 자식 노드에 넣으면 살아남지만 본체와
    // 고스트가 각각 값을 들게 되어 둘이 갈릴 수 있다 — 한 값을 양쪽이 읽게 하려고 SO로 올렸다.
    //
    // 배치 판정과는 무관하다. 점유 단위는 여전히 축 정렬 셀이고(`RebuildFootprint`는 그리드 축으로
    // 셀을 뻗는다) 조준도 대상 위치에서 계산하므로, 이 값은 **보이는 각도만** 바꾼다.
    [Tooltip("배치 시 모델을 Y축으로 돌릴 각도(도). 0 = 그리드 축 정렬(기본). " +
             "활대·포신이 넓어 인접 배치 시 겹치는 모델에만 쓴다.")]
    public float PlacementYaw;

    [HideInInspector]
    public TowerData Data;

    public List<ResourceCost> Cost;

    [Header("도감")]
    [Tooltip("타워 등급: 일반 0, 희귀 1, 전설 2")]
    public TowerRarity Rarity;

    // 타워 선택 패널에서 이 타워가 열리는 웨이브(#424). 여기 두는 이유는 `Icon`과 같다 —
    // 표기·저작 값의 소스를 SO 한 곳에 둔다. 씬 인스펙터의 타워 목록에 병렬 배열로 두면
    // 순서가 어긋나는 순간 조용히 엉키고(WL-076 축), TowerTable.csv는 로컬라이즈 키와
    // 그리드 크기 전용이라 여기가 맞다.
    [Header("해금")]
    [Tooltip("이 웨이브 번호 이상에서 해금된다. 1 = 처음부터 열림(기본).")]
    [Min(1)]
    public int UnlockWave = 1;

    // ── 평탄 스키마 (#274 Phase 1) ──────────────────────────────────────────────
    // 타입별 래퍼(Single/Area/Chain/Magic)를 풀어 한 층으로 편다. 타입별 필드가 7개뿐이라
    // 그룹 클래스 없이도 읽을 만하고, 안 쓰는 타워에서 값이 0이어도 아무도 안 읽으므로 무해하다.
    // 자세한 근거: Docs/Core/TowerRedesign.md §6
    [Header("공격")]
    public AttackFields Attack;

    // ── 조준 정책 (#387) ────────────────────────────────────────────────────────
    // "사거리 안의 여러 적 중 누구를 겨누는가". **비워두면 앞선 적**(본진에 가장 가까운 적)이다 —
    // 누수 방지가 타워의 존재 이유라 그것이 장르 기본값이다. 예전 거동(최근접)을 원하는 타워는
    // `NearestTargeting`을 명시적으로 고를 것. 새 방식이 필요하면 TargetingPolicy 파생 1개를 추가한다.
    //
    // `Attack` 안이 아니라 여기 있는 이유: 조준은 공격 액션이 아니라 **타워**의 성질이다.
    // 포탑 조준 연출(TowerTurretAim)도 같은 값을 따라가야 겨눈 적과 맞는 적이 갈라지지 않는다
    // — 그래서 소비 지점이 `Tower.AcquireTarget` 하나로 유지된다(#336).
    [Header("조준")]
    [SerializeReference] public NorthLand.Combat.TargetingPolicy Targeting;

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

    // 착탄 지점에 남는 지속 구역(#336). 화상 구역이 첫 소비처다.
    //
    // ⚠ `DebuffAura`(타워 중심 고정 장판)와 **다른 축이다** — 이쪽은 중심이 착탄점이고 수명이 있다.
    // 무엇을 거는지는 두 축 모두 아래 `Effects`가 정하므로, 같은 "화상"이 장판도 되고 착탄 구역도 된다.
    [Header("착탄 지속 구역")]
    public GroundZoneFields GroundZone;

    // 전투 실적으로 이 타워가 스스로 강해지는 축(#300). 지금까지 타워 스탯을 바꾸는 소스는 전부
    // 외부(타일 버프·오라·스킬·보스 봉인)에서 왔는데, 이것이 **자기 실적이 원장에 얹히는 첫 소스**다.
    [Header("성장(램프업)")]
    public RampFields Ramp;

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
    public float PreviewRadius => Mathf.Max(AttackSideRadius, AuraSideRadius);

    /// `TowerStat.AttackRange` 축이 붙는 쪽 기본값(공격·빔).
    ///
    /// 두 축을 따로 노출하는 이유: 프리뷰가 타일 버프를 얹을 때 **어느 축의 modifier를 쓸지** 갈라야 한다.
    /// 합친 값 하나만 들고 있으면 배치 프리뷰 원과 배치 후 실제 반경이 어긋난다(오라 타워는 사거리 축
    /// modifier를 받지 않으므로, 사거리 축을 그냥 얹으면 프리뷰만 부풀어 보인다).
    public float AttackSideRadius => Mathf.Max(
        Attack != null ? Attack.AttackRange : 0f,
        Beam != null ? Beam.Range : 0f);

    /// `TowerStat.AuraRadius` 축이 붙는 쪽 기본값(버프·디버프 오라).
    public float AuraSideRadius => Mathf.Max(
        BuffAura != null ? BuffAura.Radius : 0f,
        DebuffAura != null ? DebuffAura.Radius : 0f);

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
        bool hasAttack = false, hasBuff = false, hasDebuff = false, hasBeam = false, hasRamp = false;
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
            hasRamp |= a is NorthLand.Combat.RampAction;
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

        // ── 밸런싱 규약 ①(#326) — 간격 ≤ 사거리(타일) ÷ 3 ────────────────────
        //
        // 정본은 Docs/Core/CombatBalance.md §2, 저작 절차는 TowerAddGuide.md §3.5.
        // 어기면 **적이 사거리를 지나는 동안 발사 횟수가 1~2발에 그친다.** 발사 수는 정수이므로
        // 사거리 진입 시점의 쿨다운 위상에 따라 "쏘거나 안 쏘거나"가 갈리고, 실제 화력이 설계값의
        // 70~140% 사이에서 튄다 — 예외도 로그도 없이 "가끔 안 잡힌다"로만 드러나는 유형이다.
        // (#326 이전 cannon_tower가 정확히 이 상태였다: 사거리 2.33타일 / 간격 2.0초 → 1.4발)
        //
        // ⚠ 18 = 타일 6유닛 × 규약 계수 3. 타일 크기가 바뀌면 이 상수도 함께 바뀐다 —
        //   `OnValidate`는 씬 없이도 돌아 CombatMapGenerator.Settings를 참조할 수 없으므로
        //   단일 출처(WL-034)를 쓰지 못하고 여기 상수로 둔다.
        // ⚠ **CC 항**(#441). 규약의 유도는 "적이 사거리를 **등속으로** 통과한다"를 전제하는데,
        //   스턴 타워는 자기가 그 전제를 깬다 — 맞을 때마다 대상이 멈춰 체류 시간이 스스로 늘어난다.
        //     체류 = 통과시간 + 스턴지속 × 발수     →     발수 = 통과시간 / (간격 − 스턴지속)
        //   통과시간은 규약이 전제한 `사거리/6`초(속도 10)이므로, 3발 보장 조건이 한 항만 늘어난다:
        //     간격 ≤ 사거리/18 + 스턴지속
        //   예: 소다(사거리 27 · 스턴 0.7) → 상한 2.2초, 그 간격에서 4.5/(2.2−0.7) = 3.0발.
        //   이 항이 없으면 CC 타워는 "규약을 어기는 예외"로 관리해야 하는데, 그러면 규약이 지켜지는지
        //   자체를 검사할 수 없게 된다. 항을 세워 두면 새 CC 타워도 같은 식으로 검사된다.
        if (hasAttack && attackAuthored && Attack.AttackRange > 0f && Attack.AttackInterval > 0f)
        {
            var stun = LongestStun();
            float stunDuration = stun != null ? stun.Duration : 0f;
            float intervalLimit = Attack.AttackRange / 18f + stunDuration;

            if (Attack.AttackInterval > intervalLimit + 0.0001f)
                Debug.LogWarning($"[TowerAsset] {name}: AttackInterval({Attack.AttackInterval})이 밸런싱 규약 " +
                                 $"상한({intervalLimit:0.###})을 넘습니다 — 사거리 {Attack.AttackRange / 6f:0.##}타일" +
                                 (stunDuration > 0f ? $" · 스턴 {stunDuration:0.##}초" : "") +
                                 "에서 적이 지나가는 동안 발사가 3발 미만이라 쿨다운 위상에 따라 화력이 튑니다. " +
                                 "간격을 줄이고 발당 피해를 그만큼 올리세요(Docs/Core/CombatBalance.md §2).", this);

            // 위 식의 **하한**. 간격이 스턴 지속보다 짧으면 스턴이 끊기는 구간이 사실상 사라져
            // (`StatusEffectHandler`의 규칙 ①이 재적용을 에피소드 시작 기준으로 묶어 에피소드 길이는
            // 늘지 않지만, 간격이 더 짧으면 만료 직후 바로 다음 에피소드가 열린다)
            // 대상이 전진하지 못한다. 밤 종료 조건이 몬스터 전멸이라 딜 공급이 없으면 밤이 끝나지 않는다.
            if (stunDuration > 0f && Attack.AttackInterval <= stunDuration)
                Debug.LogWarning($"[TowerAsset] {name}: AttackInterval({Attack.AttackInterval})이 스턴 지속" +
                                 $"({stunDuration:0.##}초)보다 짧거나 같습니다 — 대상이 사실상 계속 정지 상태가 되어 " +
                                 "전진하지 못합니다. 간격을 스턴 지속보다 길게 두세요.", this);

            // 같은 식의 나머지 경계. 면역 창이 `간격 − 스턴지속`보다 길면 **다음 발이 창 안에 들어와**
            // 스턴이 걸리지 않는다 — 피해는 그대로 들어가고 CC만 조용히 절반 이하로 떨어지므로
            // 저작자가 알아채기 어렵다. 창은 다기(多機) 상한을 위한 값이지, 1기의 발사를 버리는
            // 장치가 아니다(StatusEffectHandler의 규칙 ②).
            if (stun != null && stunDuration > 0f && Attack.AttackInterval > stunDuration &&
                stun.ImmunityWindow > Attack.AttackInterval - stunDuration + 0.0001f)
                Debug.LogWarning($"[TowerAsset] {name}: StunStatus.ImmunityWindow({stun.ImmunityWindow})이 " +
                                 $"간격−스턴지속({Attack.AttackInterval - stunDuration:0.###})보다 깁니다 — " +
                                 "다음 발이 면역 창 안에 들어와 스턴이 걸리지 않는 발사가 생깁니다. " +
                                 "창을 줄이거나 간격을 늘리세요.", this);
        }

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

        // ── 연발(버스트) 저작 규칙(#336) ────────────────────────────────────
        if (hasAttack && attackAuthored && Attack.BurstCount > 1 && Attack.BurstInterval <= 0f)
            Debug.LogWarning($"[TowerAsset] {name}: BurstCount({Attack.BurstCount})>1인데 BurstInterval이 " +
                             $"{Attack.BurstInterval}입니다 — 연발이 시간차 없이 한 프레임에 몰려 나가 " +
                             "산탄과 같아지고, 착탄 구역도 한 점에 겹칩니다.", this);
        if (!hasAttack && Attack != null && Attack.BurstCount > 1)
            Debug.LogWarning($"[TowerAsset] {name}: BurstCount를 적었는데 프리팹에 AttackAction이 없습니다 " +
                             "— 이 수치는 아무도 읽지 않습니다.", this);

        // ── 착탄 지속 구역(#336) ─────────────────────────────────────────────
        // 저작 판정은 ZonePrefab 하나로 한다(GroundZoneFields 주석 참조). 아래는 전부 "스폰은 되는데
        // 아무 일도 안 나는" 또는 "영영 안 생기는" 조합이다.
        bool zonePrefabSet = GroundZone != null && GroundZone.ZonePrefab != null;

        if (zonePrefabSet && !GroundZone.IsAuthored)
            Debug.LogWarning($"[TowerAsset] {name}: GroundZone.ZonePrefab은 지정됐는데 " +
                             $"Radius({GroundZone.Radius})/Duration({GroundZone.Duration})/" +
                             $"Interval({GroundZone.Interval}) 중 0이 있습니다 — 구역이 생기지 않거나 " +
                             "생겨도 아무도 맞지 않습니다.", this);

        if (zonePrefabSet && !hasAttack)
            Debug.LogWarning($"[TowerAsset] {name}: 착탄 구역을 저작했는데 프리팹에 AttackAction이 없습니다 " +
                             "— 착탄이 발생하지 않으므로 구역이 영영 생기지 않습니다.", this);

        if (zonePrefabSet && (Effects == null || Effects.Count == 0))
            Debug.LogWarning($"[TowerAsset] {name}: 착탄 구역을 저작했는데 Effects가 비어 있습니다 " +
                             "— 구역이 생기기만 하고 아무 효과도 걸지 않습니다(화상 수치는 Effects가 소유).", this);

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

        // 대상별 조준 유지 램프(#300). 셋 다 "예외 없이 조용히 램프가 사라지는" 조합이다.
        bool lockRampAuthored = Beam != null && Beam.LockRamp != null && Beam.LockRamp.IsAuthored;
        if (lockRampAuthored && !hasBeam)
            Debug.LogWarning($"[TowerAsset] {name}: Beam.LockRamp를 적었는데 프리팹에 BeamAction이 없습니다 " +
                             "— 이 수치는 아무도 읽지 않습니다.", this);
        if (lockRampAuthored && Beam.LockRamp.StackInterval <= 0f)
            Debug.LogWarning($"[TowerAsset] {name}: Beam.LockRamp의 StackInterval이 0 이하입니다 — " +
                             "이 램프는 경과 시간으로 단계를 세므로 배율이 영원히 ×1에 머뭅니다.", this);

        // ── 성장(램프업) ↔ 수치 짝 검사(#300) ────────────────────────────────
        bool rampAuthored = Ramp != null && Ramp.Profile != null && Ramp.Profile.IsAuthored;
        if (hasRamp && !rampAuthored)
            Debug.LogWarning($"[TowerAsset] {name}: 프리팹에 RampAction이 있는데 Ramp 수치가 비었습니다 " +
                             "(PerStack 0 또는 MaxStacks 0) — 아무리 때려도 성장하지 않습니다.", this);
        if (!hasRamp && rampAuthored)
            Debug.LogWarning($"[TowerAsset] {name}: Ramp 수치를 적었는데 프리팹에 RampAction이 없습니다 " +
                             "— 이 수치는 아무도 읽지 않습니다.", this);

        // 명중 트리거는 `Projectile.DamageDealt`를 타는데, 그 이벤트는 실제 투사체를 띄우는 공격만
        // 발행한다. 히트스캔(BeamAction)은 발행하지 않는 것이 설계 의도라(TowerAddGuide.md §6)
        // "빔 타워 + 명중 램프"는 **예외도 경고도 없이 스택이 0에 머무는** 조합이 된다.
        if (hasRamp && rampAuthored && Ramp.Trigger == NorthLand.Combat.RampTrigger.Hit && !hasAttack)
            Debug.LogWarning($"[TowerAsset] {name}: Ramp.Trigger=Hit인데 프리팹에 AttackAction이 없습니다 " +
                             "— 명중 통지는 투사체 공격에서만 발행되므로 스택이 영영 쌓이지 않습니다. " +
                             "빔 타워라면 대상별 램프를 쓰거나 Trigger=Kill로 바꾸세요.", this);

        // ── 둘째 축 검사 ──────────────────────────────────────────────────
        //
        // 같은 축에 배율 modifier를 두 개 얹으면 원장이 **보너스를 합산**하므로(`TowerStats.Recompute`)
        // ×1.8이 아니라 ×2.6이 된다. 저작자 의도는 거의 확실히 "한 축을 두 번"이 아니라 오기다.
        if (Ramp != null && Ramp.HasSecondary && Ramp.SecondaryStat == Ramp.Stat)
            Debug.LogWarning($"[TowerAsset] {name}: Ramp의 두 축이 모두 {Ramp.Stat}입니다 — 원장이 배율 " +
                             "보너스를 합산하므로 의도한 배율의 두 배 가까이 오릅니다. 둘째 축을 다른 " +
                             "스탯으로 바꾸거나 SecondaryPerStack을 0으로 두세요.", this);

        // 둘째 축만 적고 주 축 수치를 비우면 램프 자체가 미저작이라(`Profile.IsAuthored`)
        // `RampAction`이 구독조차 하지 않는다 — 둘째 축도 함께 죽는다.
        if (Ramp != null && Ramp.HasSecondary && !rampAuthored)
            Debug.LogWarning($"[TowerAsset] {name}: Ramp.SecondaryPerStack을 적었는데 Ramp.Profile이 " +
                             "비었습니다(PerStack 0 또는 MaxStacks 0) — 스택 카운터가 주 축 수치에 " +
                             "달려 있어 둘째 축도 함께 동작하지 않습니다.", this);
    }

    /// 이 타워가 명중으로 거는 스턴 중 **지속이 가장 긴 것**. 스턴을 걸지 않으면 null.
    ///
    /// 규약 ①의 CC 항이 쓰는 값이다. 최댓값을 쓰는 이유: 대상은 스턴을 하나만 갖고
    /// (`StatusEffectHandler`의 대상 기준 게이트) 그중 먼저 걸린 것이 만료될 때까지 유지되므로,
    /// 체류 시간을 늘리는 기여는 가장 긴 항이 대표한다.
    /// `Effects`는 `[SerializeReference]`라 rename·삭제로 항목이 null이 될 수 있어 건너뛴다.
    ///
    /// ⚠ **`Effects`가 어느 축에서 발화하는지는 구분하지 않는다.** 이 리스트는 공격·디버프 오라·착탄
    /// 구역이 함께 쓰므로(위 `Effects` 필드 주석), "오라로만 스턴을 거는데 공격도 하는" 타워가 생기면
    /// 명중은 등속 통과 그대로인데 `AttackInterval` 상한만 완화된다. **지금은 성립하지 않는 전제다** —
    /// 스턴원은 `soda_tower`(0.7초)·`soda_cannon_tower`(1.0초) 2종이고 **둘 다 명중 경로**다(#478).
    /// 스턴원이 늘어난 것 자체는 전제를 깨지 않는다 — 깨지는 것은 오라·구역으로 스턴을 거는 타워가
    /// 생길 때이고, 그날 축을 갈라야 한다(`Effects`를 명중용·오라용으로 분리하는 작업이라 범위가 크다).
    NorthLand.Combat.StunStatus LongestStun()
    {
        if (Effects == null) return null;

        NorthLand.Combat.StunStatus longest = null;
        for (int i = 0; i < Effects.Count; i++)
        {
            if (Effects[i] is NorthLand.Combat.StunStatus stun &&
                (longest == null || stun.Duration > longest.Duration))
                longest = stun;
        }
        return longest;
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

        // ── 연발(버스트, #336) ───────────────────────────────────────────────
        // 산탄(`PelletCount`)과 **다른 축이다**: 산탄은 한 순간에 부채꼴로 여러 발, 버스트는 같은 조준으로
        // **시간을 두고** 여러 발이다. 그래서 착탄 지점이 갈리고(그 사이 적이 움직인다) 착탄 구역이
        // 발수만큼 생긴다 — 산탄은 유도·곡사와 조합하면 펠릿이 한 점으로 수렴해 그것이 성립하지 않는다.
        // 기본값 1 = 발사당 1발, 기존 전 타워 거동 무변경.
        [Tooltip("한 번의 공격 사이클에 연달아 쏘는 발수. 1이면 기존 거동과 동일.")]
        public int BurstCount = 1;

        [Tooltip("BurstCount>1일 때 연발 사이 간격(초). 이 값은 공속 원장을 거치지 않는다 — " +
                 "연발 리듬은 버프로 흔들리지 않는 편이 예측 가능하다.")]
        public float BurstInterval;
    }

    // 착탄 지점에 남는 지속 구역의 저작 묶음(#336).
    //
    // ⚠ **수치 기본값이 전부 0이다.** 이 블록은 기존 SO 18종에도 생기므로(직렬화 필드 추가),
    // 기본값이 0이 아니면 "수치는 있는데 프리팹이 없다"는 경고가 전 타워에서 뜬다.
    // 저작 여부의 유일한 판정 기준은 `ZonePrefab`이다.
    [System.Serializable]
    public class GroundZoneFields
    {
        [Tooltip("착탄 지점에 띄울 이펙트 프리팹. 우리 스크립트가 없어도 된다 — 런타임에 붙는다.")]
        public GameObject ZonePrefab;

        [Tooltip("효과를 적용할 반경. ⚠ 이펙트 프리팹의 보이는 크기와 맞춰 저작할 것 — 자동으로 맞지 않는다.")]
        public float Radius;

        [Tooltip("구역이 남아 있는 시간(초).")]
        public float Duration;

        [Tooltip("반경 안 적에게 효과를 재적용하는 주기(초). 화상 수치 자체는 Effects가 소유한다.")]
        public float Interval;

        public bool IsAuthored => ZonePrefab != null && Radius > 0f && Duration > 0f && Interval > 0f;
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

        [Tooltip("틱마다 각 대상에게 들어가는 피해. LockRamp 미저작이면 대상별로 동일하다.")]
        public float DamagePerTick;

        // 대상별 조준 유지 램프(#300). **미저작(MaxStacks 0)이면 기존 균일 지속딜 그대로**라
        // multi_inferno_tower의 거동이 변하지 않는다. MaxTargets=1 + 이 값 = 단일 인페르노.
        //
        // ⚠ 이 램프는 `Ramp`(원장형)와 **다른 축**이다. 원장은 타워 단위라 대상이 바뀌어도 배율이
        // 유지되고 그 타워의 다른 액션·DoT까지 함께 강해진다 — "한 놈을 오래 지질수록 아프다"는
        // 정체성은 그렇게 표현할 수 없다. 그래서 BeamAction이 대상별로 따로 센다.
        [Tooltip("같은 대상을 계속 잠글수록 피해가 오르는 램프. StackInterval초마다 한 단계씩 오르고, " +
                 "대상이 죽거나 사거리를 벗어나면 0에서 다시 시작한다. 비워두면 균일 지속딜(기존 거동).")]
        public NorthLand.Combat.RampProfile LockRamp;

        // 램프 진행도에 따른 빔 겉모습(#426). **프리팹 컴포넌트가 아니라 여기 있는 이유가 두 가지다.**
        //
        // ① 액션 규칙 ① — "액션은 수치를 갖지 않는다. 전부 TowerAsset에서 읽는다." 굵기·색도 수치이므로
        //    같은 규칙을 받는다. `Icon`·`ProjectilePrefab`·`PlacementYaw`가 이미 같은 성격으로 이 SO에 있다.
        // ② **저장소 교차 참조 제거(WL-196)** — 이 값을 프리팹에 두면 Imported 프리팹이 본 저장소의
        //    MonoBehaviour를 참조하게 되어, 다른 에셋과 **반대 방향**의 머지 순서(본 저장소 선행)가
        //    필요해진다. Imported만 먼저 받으면 Missing Script가 되고 재직렬화 시 배선이 조용히 날아간다.
        //    SO에 두면 참조가 본 저장소 → Imported 한 방향으로만 흘러 기존 규칙(Imported 선행)이 그대로
        //    성립하고, 이 연출을 본 저장소 diff만으로 검토할 수 있다.
        [Tooltip("램프 진행도별 빔 굵기·색. 비워두면 단색 기본 연출(기존 거동). " +
                 "낮은 문턱부터 순서대로 저작할 것.")]
        public List<BeamStage> VisualStages = new List<BeamStage>();
    }

    /// 빔 연출 한 구간. 램프 진행도(0~1)가 이 문턱 이상이면 이 겉모습으로 그린다.
    ///
    /// **연속 보간이 아니라 단계인 이유**: 램프가 오르고 있다는 것을 플레이어가 알아채야 하는 연출이다.
    /// 굵기를 연속으로 키우면 프레임당 변화가 미세해 "세지고 있다"가 읽히지 않는다 — 전환 순간이
    /// 사건이 되어야 눈에 걸린다(COC 인페르노가 같은 이유로 단계형이다).
    ///
    /// ⚠ **최고 단계 문턱을 1.0에 붙이지 말 것.** `single_inferno`는 만렙까지 3.36초가 걸리는데 적 체류가
    /// 3.33초라 마지막 스택에 도달하지 못한다(CombatBalance.md §3.2). 문턱이 1.0 근처면 그 단계가
    /// 화면에 영원히 안 나온다.
    [System.Serializable]
    public class BeamStage
    {
        [Tooltip("에디터에서 알아보기 위한 이름. 런타임에는 쓰지 않는다.")]
        public string Label;

        [Tooltip("이 단계가 시작되는 진행도(0~1). 진행도 이하 중 **가장 높은** 문턱의 단계가 쓰인다.")]
        [Range(0f, 1f)] public float FromProgress;

        [Tooltip("빔 굵기(월드 단위).")]
        public float Width = 0.5f;

        [Tooltip("빔 색.")]
        [ColorUsage(true, true)] public Color Color = Color.white;
    }

    // 성장(램프업) 저작 묶음(#300). 계기와 축은 여기서, 수치는 공용 부품 `RampProfile`이 소유한다 —
    // 같은 수치 규약을 대상별 램프와 공유하기 위해서다.
    [System.Serializable]
    public class RampFields
    {
        [Tooltip("무엇이 성장하는가. 명중 램프=AttackSpeed, 처치 램프=AttackDamage를 기본 상정.")]
        public NorthLand.Combat.TowerStat Stat = NorthLand.Combat.TowerStat.AttackSpeed;

        [Tooltip("스택을 얻는 계기. Hit=투사체 명중(빔 제외), Kill=적 처치.")]
        public NorthLand.Combat.RampTrigger Trigger = NorthLand.Combat.RampTrigger.Hit;

        public NorthLand.Combat.RampProfile Profile = new NorthLand.Combat.RampProfile();

        // ── 둘째 축(#441 후속) ─────────────────────────────────────────────
        //
        // **스택 카운터는 하나다.** 같은 명중 1회가 두 스탯을 함께 올리며, 트리거·상한·감쇠는
        // `Profile`이 단독으로 정한다 — 축마다 다른 속도로 자라게 하려면 `Ramp`를 리스트로 바꿔야
        // 하고, 그러면 `RampAction`의 단일 스택 카운터 구조가 무너진다(별도 이슈 규모).
        //
        // ⚠ `SecondaryPerStack == 0`이 **미저작**이다(`RampProfile.IsAuthored`와 같은 관례).
        //   그래서 이 두 필드를 추가해도 기존 SO는 전부 단일 축 거동을 유지한다 — 직렬화가
        //   가산이라 `SecondaryStat`은 기본값 0(AttackDamage)으로 읽히지만 배율이 0이라 무동작이다.
        [Tooltip("같은 스택을 얹을 둘째 스탯. 아래 SecondaryPerStack이 0이면 무시된다(단일 축).")]
        public NorthLand.Combat.TowerStat SecondaryStat = NorthLand.Combat.TowerStat.AttackDamage;

        [Tooltip("둘째 축의 스택당 보너스. 0 = 둘째 축 없음(기존 거동). 0.04 = 스택당 +4%.")]
        public float SecondaryPerStack;

        /// 둘째 축이 실제로 동작하는가. 스택 상한·감쇠는 `Profile`이 공유하므로 여기서는
        /// 배율만 본다 — `Profile.IsAuthored`가 이미 거짓이면 램프 자체가 무동작이다.
        public bool HasSecondary => SecondaryPerStack != 0f;

        /// 둘째 축의 실효 배율. 스택 환산은 `Profile`의 상한을 그대로 쓴다(카운터 공유).
        public float SecondaryMultiplier(int stacks)
            => Profile == null ? 1f : 1f + Profile.Clamp(stacks) * SecondaryPerStack;
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
