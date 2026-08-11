using System;
using System.Collections.Generic;
using UnityEngine;
using CombatSpace;

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

        // 포신이 둘 이상인 타워(양날개 미사일 터렛 등)의 발사 위치 목록(#336). 비워두면 위의 단일
        // `firePoint`가 쓰이므로 **기존 프리팹은 전부 무변경**이다.
        //
        // 단일 필드를 배열로 바꾸지 않고 나란히 두는 이유: 기존 프리팹 18종의 인스펙터 배선이
        // 그 자리에 남아 있어야 하고(배열로 옮기면 전부 다시 물려야 한다), 빔·레이저처럼 "포구가
        // 하나뿐인 개념"은 여전히 단일 값을 읽기 때문이다(BeamAction·LaserAction의 `Owner.FirePoint`).
        [SerializeField] Transform[] firePoints;

        // ★ 이 타워가 "무엇을 하는 물건인지"를 담는 액션들 — **프리팹이 정본이다**(#274).
        //
        // 인스펙터에서 `+ Attack Action` / `+ Buff Aura Action`을 골라 담는다. 예전에는 SO의 TowerType을
        // 보고 TowerBehaviourFactory가 런타임에 AddComponent로 만들었는데, 그래서 ① 프리팹만 봐선 무슨
        // 타워인지 알 수 없고 ② 새 종류를 만들 때 enum·팩토리·에디터 3곳을 고쳐야 했다.
        //
        // 단일 참조가 아니라 리스트인 이유: `if (attack != null)` 분기가 누적돼 이 클래스가 다시
        // 만능 클래스로 돌아가는 것을 구조적으로 막고, **공격+오라 하이브리드 타워를 공짜로 허용**한다.
        [SerializeReference] List<TowerAction> actions = new List<TowerAction>();

        // 조립 여부. Build를 두 번 타지 않게 하고, OnEnable 폴백 경로의 판단 근거가 된다.
        bool built;

        // ── 합성 효과 계승 (#274 Phase 5) ──────────────────────────────────
        // 이 인스턴스에서 활성인 효과 종류. **null = 필터 없음**(SO의 Effects가 전부 활성)이고,
        // 합성으로 만들어진 타워만 ActivateEffects로 이 집합을 갖는다.
        //
        // ★ SO가 아니라 인스턴스가 소유해야 하는 이유 두 가지(TowerRedesign.md §9.2):
        //  ① SO 오염 — TowerAsset은 씬의 모든 인스턴스가 공유한다. 런타임에 data.Effects를 건드리면
        //     다음 합성이 이전 계승분을 물려받고, [SerializeReference]라 진짜로 직렬화되어
        //     .asset 파일에 **영구히** 남는다.
        //  ② 다단 합성 — 합성 결과도 다시 재료가 될 수 있다. "재료 A가 무엇을 갖는가"를 판정하려면
        //     A의 SO가 아니라 **A 인스턴스의 활성 상태**를 읽어야 한다(SO를 읽으면 꺼진 것까지 잡힌다).
        //
        // 런타임 상태이므로 직렬화하지 않는다(액션 규칙 ③과 같은 축).
        [NonSerialized] HashSet<EffectKind> activeKinds;

        /// 이 효과 종류가 이 타워에서 활성인가. 필터가 없으면(=합성 산물이 아니면) 전부 활성이다.
        /// 효과를 **거는 순간** 호출된다 — 목록을 미리 걸러두지 않는 이유는 Build가 배치 확정 콜백보다
        /// 먼저 돌아서, 액션 초기화 시점에는 아직 계승분이 정해지지 않았기 때문이다(pull 방식).
        public bool IsEffectActive(EffectKind kind) => activeKinds == null || activeKinds.Contains(kind);

        /// 이 타워가 실제로 낼 수 있는 효과 종류. **다단 합성이 재료를 조회하는 창구**다.
        /// 평범하게 배치된 타워는 자기 SO의 효과 전부를, 합성 산물은 켜진 것만 보고한다.
        public IEnumerable<EffectKind> ActiveEffectKinds
        {
            get
            {
                if (data?.Effects == null) yield break;

                for (int i = 0; i < data.Effects.Count; i++)
                {
                    HitEffect effect = data.Effects[i];
                    if (effect == null) continue;          // [SerializeReference] rename 시 null 항목
                    if (!IsEffectActive(effect.Kind)) continue;
                    yield return effect.Kind;
                }
            }
        }

        /// 합성 결과 타워에 계승된 효과 종류를 부여한다(TowerFusionController가 배치 확정 시 호출).
        /// null을 넘기면 필터가 해제되어 SO의 효과가 전부 살아난다.
        public void ActivateEffects(IEnumerable<EffectKind> kinds)
            => activeKinds = kinds == null ? null : new HashSet<EffectKind>(kinds);

        // 액션에 넘기는 프리팹 배선. 액션은 SO 수치만 읽고 씬 참조는 호스트를 통해 얻는다(규칙 ②) —
        // [SerializeReference] 관리 객체 안에 Transform 참조를 직접 두는 것은 프리팹 오버라이드에서
        // 다루기 까다롭고, 이렇게 두면 프리팹의 기존 배선 값이 제자리에 남는다.
        internal Transform FirePoint => firePoint;
        internal LayerMask EnemyLayerMask => enemyLayerMask;

        // 다음 발사에 쓸 포구. 커서를 여기서 굴리는 이유는 배선의 소유자가 호스트이기 때문이다
        // (액션 규칙 ②) — 액션이 인덱스를 관리하면 포구 개수를 알아야 하고, 그 값이 프리팹 배선이라
        // 액션이 씬 구조를 아는 셈이 된다. 커서는 직렬화되지 않는 런타임 값이다.
        //
        // 배열이 비면 단일 `firePoint`(그것도 없으면 null → 호출부가 타워 루트로 폴백)를 그대로 반환하므로
        // **기존 타워는 이 경로를 타도 거동이 바뀌지 않는다.**
        internal Transform NextFirePoint()
        {
            if (firePoints == null || firePoints.Length == 0) return firePoint;

            Transform result = firePoints[firePointCursor];
            firePointCursor = (firePointCursor + 1) % firePoints.Length;

            // 배열에 빈 슬롯이 섞여 있어도 발사가 멈추지 않게 단일 값으로 떨어진다(저작 실수 방어).
            return result != null ? result : firePoint;
        }

        int firePointCursor;

        // 저작 검증(TowerAsset.OnValidate)이 프리팹의 액션 구성을 들여다보는 창구.
        // 런타임 소비처는 능력 질의(Has/Get)를 쓴다 — 리스트를 통째로 노출하는 것은 검증·툴링용이다.
        internal IReadOnlyList<TowerAction> Actions => actions;

        /// 이 타워가 해당 액션을 가지는지. 소비처가 타워의 **구상 타입**이 아니라 **능력**을 묻게 하는 창구다
        /// (예: 보스 마력 봉인은 `Has&lt;AttackAction&gt;()`인 타워만 대상으로 한다).
        public bool Has<T>() where T : TowerAction
        {
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] is T) return true;
            }
            return false;
        }

        /// 해당 액션을 반환한다. 없으면 null.
        public T Get<T>() where T : TowerAction
        {
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] is T match) return match;
            }
            return null;
        }

        // 이 타워의 스탯 modifier 단일 원장(WL-050/WL-081). 타일 버프·버프 스킬·버프 오라·보스 마력 봉인이
        // 전부 여기로 수렴하며, 합성 규칙은 TowerStats.Evaluate 한 곳에만 산다.
        // 소스별로 합산 중첩되고 같은 소스키는 갱신만 된다. 소스키 도메인:
        //   버프 오라 = 행동 인스턴스별 → 같은 종류 버프 타워도 서로 합산 중첩됨
        //   플레이어 버프 스킬 = 고정 문자열 해시 / 보스 마력 봉인 = 에이전트ID ^ 효과종류 해시
        //   타일 버프 = TileBuffSourceId(아래 고정 상수, 지속형)
        // 공유 TowerAsset 값은 건드리지 않고 이 인스턴스의 원장만 조작한다.
        // 필드 초기화자로 즉시 준비된다 — Awake/Start를 기다리지 않으므로 Instantiate 직후
        // (TowerPlacer가 컴포넌트를 붙이는 시점)에도 안전하게 Apply할 수 있다.
        readonly TowerStats stats = new TowerStats();

        public TowerStats Stats => stats;

        // 타일 버프의 고정 소스키. 배치 시 1회 부여되는 지속형이라 인스턴스별로 채번할 필요가 없다.
        static readonly int TileBuffSourceId = "TowerTileBuff".GetHashCode();

        // 타일 버프 → 원장 변환 버퍼(재사용). 배치 시 1회만 쓰이므로 공유해도 겹치지 않는다.
        static readonly List<TowerStatModifier> TileBuffScratch = new List<TowerStatModifier>(6);

        // 씬에 존재하는 모든 Tower를 스킬 등에서 순회할 수 있게 자가 등록(FindObjectsByType 대체).
        public static readonly List<Tower> Active = new();
        // 타워가 씬에 추가/제거될 때 발생. 버프 타워가 배치 즉시(낮 포함) + layout 변화 시 사거리 내 타워를 갱신하는 데 쓴다(#164).
        public static event Action ActiveChanged;

        // 직전 프레임의 페이즈. 밤→낮 전이 = 웨이브 종료 통지의 근거다(Update 참조).
        bool wasNight;
        // Active 등록 여부. 등록 시점이 OnEnable이 아니라 Build라서, "켜졌지만 아직 조립되지 않은" 구간과
        // 재활성화를 구분해야 한다.
        bool registered;

        void OnEnable()
        {
            if (!built)
            {
                // 폴백 조립: 씬에 미리 놓인 타워·테스트 씬처럼 외부에서 Build를 불러주지 않는 경로.
                // data가 없으면 아무것도 하지 않는다 — **조립되지 않은 타워는 존재하지 않는 타워**이고,
                // 그래서 Active에도 들어가지 않는다. 이 규칙 하나가 배치 프리뷰(고스트)가 타워로
                // 집계되던 문제(WL-066)를 구조적으로 없앤다.
                if (data != null) Build(data);
                return;
            }

            // 재활성화(풀 재사용 등): OnDisable이 원장을 비웠으므로 되돌려야 한다.
            // 버프 오라가 준 modifier는 Register()→ActiveChanged→각 오라의 Reapply로 자동 복원되지만,
            // 타일 버프는 배치 시 1회 푸시라 스스로 돌아올 경로가 없다 → 여기서 다시 밀어준다.
            if (TryGetComponent(out TowerTileBuff tileBuff)) ApplyTileBuff(tileBuff.Result);

            ReinitializeActions();   // 액션을 재무장
            Register();
        }

        // 발사 시점 통지(탄약 시각 연출 등 구독용 — 예: 캐논 포탄이 발사 순간 사라짐).
        public event Action OnFired;

        // 발사 통지 발행 창구. 액션(AttackAction)이 실제 발사 주체지만, 구독자(TowerReloadVisual)는
        // 타워를 보고 붙으므로 이벤트 소유는 호스트에 남긴다.
        internal void RaiseFired() => OnFired?.Invoke();

        // ── 대상 탐색 (#336) ────────────────────────────────────────────────
        // "이 타워가 지금 누구를 겨누는가"의 **단일 출처**. 예전에는 공격 액션과 포탑 조준 연출이
        // 각자 OverlapSphere를 돌렸는데, `TowerAction.Origin`이 `Owner.transform`이라 원점·반경·마스크·
        // 판정 기준이 **완전히 같은 쿼리 두 벌**이었다. 비용도 비용이지만 진짜 문제는 "대상이 누구인가"의
        // 정의가 둘로 갈린다는 것이다 — 조준 정책을 최근접에서 다른 것으로 바꾸면 한쪽만 고쳐져
        // **포탑이 겨눈 적과 실제로 맞는 적이 달라진다**(예외도 로그도 없이 그림만 어긋난다).
        [NonSerialized] IDamageable acquired;
        [NonSerialized] int acquiredFrame = -1;
        [NonSerialized] Collider[] acquireBuffer;

        /// 사거리 안에서 가장 가까운 적. **프레임당 실제 조회는 1회**고 같은 프레임의 이후 호출은
        /// 캐시를 돌려준다 — 소비처들이 서로의 호출 주기를 몰라도 되게 하는 장치다.
        /// (발사 프레임에는 액션이 Update에서 먼저 계산하고, 포탑 연출이 LateUpdate에서 그 값을 받는다.)
        ///
        /// 사거리는 원장 합성값(`AttackRange`)이라 사거리 버프가 조준 범위에도 그대로 반영된다.
        /// 공격 액션이 없으면 0이 되어 조회 없이 곧바로 null이다.
        public IDamageable AcquireTarget()
        {
            if (acquiredFrame == Time.frameCount) return acquired;

            acquiredFrame = Time.frameCount;
            acquired = FindNearestEnemy(AttackRange);
            return acquired;
        }

        IDamageable FindNearestEnemy(float range)
        {
            if (range <= 0f) return null;

            acquireBuffer ??= new Collider[16];

            Vector3 origin = transform.position;
            int count = Physics.OverlapSphereNonAlloc(origin, range, acquireBuffer, enemyLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider hit = acquireBuffer[i];
                IDamageable damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable == null || damageable.IsDead) continue;
                if (damageable.Faction == Faction) continue;

                float sqrDistance = (hit.transform.position - origin).sqrMagnitude;
                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closest = damageable;
                }
            }

            return closest;
        }

        /// 지금이 전투 시간(밤)인가. **호스트가 이미 계산해 둔 값을 그대로 공개한다** — 연출 컴포넌트가
        /// 각자 `DayNightManager`를 폴링하면 페이즈 규칙이 갈라지고(WL-044가 지적한 축), "게이팅은 밤인데
        /// 연출은 낮"처럼 어긋난 상태가 생긴다. 신호원을 하나로 유지하려고 읽기 전용으로 열어 둔다.
        public bool IsCombatPhase => wasNight;

        void OnDisable()
        {
            // 액션이 외부에 남긴 상태(버프 오라가 다른 타워에 부여한 modifier, static 이벤트 구독 등)를
            // 먼저 걷어낸다. Unregister보다 앞이어야 한다 — 오라가 대상 목록을 다시 계산할 때 자기 자신이
            // 아직 목록에 있어도 무해하지만, 반대 순서면 통지를 받은 오라가 이미 사라진 상태를 볼 수 있다.
            for (int i = 0; i < actions.Count; i++) actions[i]?.Dispose();

            Unregister();

            // 풀링 등으로 재활성화됐을 때 버프가 고착되지 않도록 원장을 비운다(PR#115 리뷰 지적).
            stats.Clear();
        }

        void Register()
        {
            if (registered) return;

            registered = true;
            Active.Add(this);
            ActiveChanged?.Invoke();
        }

        void Unregister()
        {
            if (!registered) return;

            registered = false;
            Active.Remove(this);
            ActiveChanged?.Invoke();   // 철거 등으로 빠지면 버프 오라가 대상 목록을 다시 계산하도록 통지.
        }

        /// 이 타워를 SO대로 조립한다. **타워가 무엇을 하는 물건이 되는지 결정하는 유일한 지점**이며,
        /// TowerPlacer(배치)·합성·테스트 씬이 전부 여기를 통과한다.
        /// 이미 조립된 상태에서 다시 부르면 행동을 새 SO로 재구성한다(합성 결과 배치·풀 재사용).
        public void Build(TowerAsset asset)
        {
            if (asset == null)
            {
                Debug.LogError("[Tower] TowerAsset 없이 Build할 수 없습니다.", this);
                return;
            }

            // 이미 같은 SO로 조립돼 있으면 행동 구성은 그대로 두고 재무장만 한다(배치 경로에서 프리팹
            // 자가조립 뒤 곧바로 들어오는 흔한 경우). 조기 반환하지 않고 Initialize를 다시 부르는 이유:
            // Build는 "부르고 나면 이 타워는 완전히 동작 가능하다"를 보장해야 한다 — Dispose된 상태에서
            // 들어왔을 때 조용히 죽은 채로 남으면 호출부가 그것을 알 방법이 없다.
            if (built && data == asset)
            {
                ReinitializeActions();
                Register();
                return;
            }

            // 여기 도달했는데 이미 조립돼 있다면 "프리팹이 문 SO ≠ 실제로 배치되는 SO"라는 뜻이다.
            // 예전에는 이 불일치가 무증상이었다(WL-129) — 배치는 산 SO로, 인스턴스는 프리팹 SO로 동작.
            // 이제 산 SO가 이기고, 저작 실수는 이 경고로 드러난다.
            if (built && data != null)
            {
                Debug.LogWarning(
                    $"[Tower] 프리팹이 문 TowerAsset({data.TowerID})과 배치된 TowerAsset({asset.TowerID})이 다릅니다. " +
                    "배치된 쪽으로 재조립합니다 — 프리팹의 data 참조를 확인하세요.", this);
            }

            // 재조립이면 이전 액션이 외부에 남긴 상태를 먼저 걷어낸다.
            for (int i = 0; i < actions.Count; i++) actions[i]?.Dispose();

            data = asset;
            built = true;

            // 다른 SO로 재조립되면 이전 정체성의 계승분은 의미를 잃는다(풀 재사용 대비).
            // ⚠ OnDisable/OnEnable 왕복에서는 **지우지 않는다** — 합성 커맨드의 Release/Reoccupy가
            //   그 경로를 쓰므로, 지우면 롤백된 합성 결과가 효과를 잃는다.
            activeKinds = null;

            // 액션을 만들지 않는다 — 프리팹에 이미 담겨 있다. 예전에는 여기서 TowerBehaviourFactory가
            // SO의 TowerType을 보고 AddComponent로 조립하고, 새 SO에서 빠진 컴포넌트를 다시 떼어냈다
            // (StripUnusedBehaviourComponents). **프리팹이 정본이면 "SO에서 빠진 행동"이라는 개념 자체가 없다.**
            ReinitializeActions();
            Register();

            // ⚠ **무증상 조합의 유일한 런타임 신호다.** 액션이 없으면 Update가 첫 줄에서 return하므로
            // 이 타워는 예외도 경고도 없이 아무 동작을 하지 않는다 — "배치는 되는데 안 쏜다"의 1순위 원인.
            //
            // 가장 흔한 경위: 타워 프리팹이 `Assets/Imported/`(부모 저장소 미추적, 자체 중첩 git)에 살아서
            // 그 저장소를 동기화하지 않은 환경에서는 프리팹의 Actions가 빈 채로 로드된다.
            // 저장 시점 검증(TowerAsset.OnValidate)은 TowerPrefab이 아예 null이면 잡지 못하므로
            // 에디터를 안 켜는 사람에게는 이 로그가 유일한 단서다.
            if (actions.Count == 0)
            {
                Debug.LogWarning(
                    $"[Tower] {name}({asset.TowerID}): Actions가 비어 있어 아무 동작도 하지 않습니다 — " +
                    "프리팹에 TowerAction이 담겼는지, `Assets/Imported/`가 최신인지 확인하세요.", this);
            }
        }

        void ReinitializeActions()
        {
            for (int i = 0; i < actions.Count; i++) actions[i]?.Initialize(this, data);
        }

        // 배치 시 확정된 타일 버프를 원장에 지속형 소스로 밀어 넣는다(TowerTileBuff.Initialize가 호출).
        // 예전처럼 스탯 게터가 매번 TowerTileBuff를 되읽지 않는 이유: 그러면 초기화 순서(TowerPlacer가
        // AddComponent→Initialize하는 시점 vs OnEnable/Start)에 결과가 좌우된다 — AuraTower가 바로 그
        // 순서 때문에 Start에서 반경을 재계산하는 우회로를 두고 있었다. 푸시로 바꾸면 순서 의존이 사라진다.
        public void ApplyTileBuff(TileBuffCalculationResult result)
        {
            if (result == null)
            {
                stats.Remove(TileBuffSourceId);
                return;
            }

            TileBuffScratch.Clear();
            AddTileModifier(result, TileBuffStat.AttackDamage, TowerStat.AttackDamage);
            AddTileModifier(result, TileBuffStat.AttackRange, TowerStat.AttackRange);
            AddTileModifier(result, TileBuffStat.AttackSpeed, TowerStat.AttackSpeed);

            // duration<=0 = 지속형. 타일 버프는 타워가 그 타일 위에 있는 한 유지된다.
            stats.Apply(TileBuffSourceId, TileBuffScratch, 0f, Time.time);
        }

        // TileBuffStat/TileModifierMode → 원장 축 변환. 0인 항목은 담지 않는다(죽은 modifier 방지).
        static void AddTileModifier(TileBuffCalculationResult result, TileBuffStat from, TowerStat to)
        {
            float flat = result.GetValue(from, TileModifierMode.Flat);
            if (flat != 0f) TileBuffScratch.Add(new TowerStatModifier(to, TowerModifierMode.Flat, flat));

            float percentage = result.GetValue(from, TileModifierMode.Percentage);
            if (percentage != 0f) TileBuffScratch.Add(new TowerStatModifier(to, TowerModifierMode.Percentage, percentage));
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

            // `?.`가 필수다 — DataTableManager.Get<T>는 테이블 미등록 시 LogError 후 **null을 반환**한다.
            // 이게 없으면 TowerTable이 등록되지 않은 씬(테스트 씬 등)에서 타워를 클릭할 때 NRE가 나고,
            // 바로 아래 null 가드는 도달조차 못 한다. 나머지 3개 채움 지점은 전부 `?.Get`을 쓴다.
            data.Data ??= DataTableManager.Get<TowerTable>("TowerTable")?.Get(data.TowerID);
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
            // `?.`는 순수 참조 검사라 **파괴된** 원(타워 소모·철거로 자식째 파괴)도 통과시켜 Hide() 안에서
            // MissingReferenceException이 난다 → Unity 오버로드 ==로 검사한다.
            if (_rangeCircle != null) _rangeCircle.Hide();
            TowerInfoUI.Instance.HideInfo();
        }

        // 선택 시 사거리 원(버프 반영 AttackRange)을 표시한다. 원은 자식 GO라 타워 파괴 시 함께 정리된다.
        void ShowRangeCircle()
        {
            if (_rangeCircle == null)
                _rangeCircle = RangeCircle.Create(transform, selectionRangeFillColor, selectionRangeColor, "TowerRangeSelection");

            _rangeCircle.SetRadius(DisplayRange);
            _rangeCircle.Show();
        }

        // 표시용 반경 = 액션들이 보고한 값 중 최대. `AttackRange`를 쓰면 공격 액션이 없는 오라 타워에서
        // 0이 되어 원이 아예 그려지지 않는다(#192 회귀). 최대값을 쓰는 이유는 공격+오라 하이브리드 타워에서
        // 더 넓은 쪽이 플레이어가 알아야 할 영향 범위이기 때문.
        float DisplayRange
        {
            get
            {
                float result = 0f;
                for (int i = 0; i < actions.Count; i++)
                {
                    if (actions[i] == null) continue;
                    float range = actions[i].DisplayRange;
                    if (range > result) result = range;
                }
                return result;
            }
        }

        // 정보 패널용 스탯 텍스트. 각 액션이 자기 설명을 만들고 호스트는 붙이기만 한다 —
        // 타워가 "무엇을 하는 물건인지" 알아야 할 이유가 표시 경로에도 없다.
        // 기여할 액션이 하나도 없으면 null(패널은 통계 구간 없이 설명만 표시).
        string BuildStatsText()
        {
            string result = null;

            for (int i = 0; i < actions.Count; i++)
            {
                string piece = actions[i]?.DescribeStats();
                if (string.IsNullOrEmpty(piece)) continue;

                result = result == null ? piece : $"{result}\n{piece}";
            }

            return result;
        }

        // ── IAttacker 계약 ────────────────────────────────────────────────
        // 값은 공격 행동이 소유한다(기본값=SO, modifier=원장). 이 타워가 공격하지 않으면 전부 0 —
        // "공격 스탯이 없는 타워"라는 상태를 별도 분기 없이 값으로 표현한다.
        public float AttackDamage => Get<AttackAction>()?.Damage ?? 0f;

        public float AttackRange => Get<AttackAction>()?.Range ?? 0f;

        public float AttackInterval => Get<AttackAction>()?.Interval ?? 0f;

        // 버프 진입점(플레이어 스킬 #103 / 버프 타워 #164 공용). sourceId별로 항목을 add/refresh하며,
        // 서로 다른 소스는 합산 중첩된다(같은 sourceId는 갱신만). 배율(예: 1.2)은 보너스(0.2)로 저장해
        // 실효 배율 = 1 + 보너스 합. duration>0이면 그 시간 후 만료(Update에서 정리), duration<=0이면
        // 지속형(만료 없음 → RemoveBuff로만 해제). 이벤트형 버프 타워는 지속형, 스킬은 시간제로 쓴다.
        public void ApplyBuff(int sourceId, float damageMul, float attackSpeedMul, float duration)
        {
            BuffScratch.Clear();
            BuffScratch.Add(new TowerStatModifier(TowerStat.AttackDamage, TowerModifierMode.Multiplier, damageMul));
            BuffScratch.Add(new TowerStatModifier(TowerStat.AttackSpeed, TowerModifierMode.Multiplier, attackSpeedMul));

            stats.Apply(sourceId, BuffScratch, duration, Time.time);
        }

        // 특정 소스의 버프를 즉시 제거(만료 대기 없이). 이벤트형 버프 타워가 밤 종료·비활성화 시 호출한다.
        public void RemoveBuff(int sourceId) => stats.Remove(sourceId);

        // ApplyBuff 변환 버퍼(재사용). 프레임 내 순차 호출이고 Apply가 반환 전에 소비하므로 공유해도 겹치지 않는다.
        static readonly List<TowerStatModifier> BuffScratch = new List<TowerStatModifier>(2);

        void Update()
        {
            // 만료된 소스를 정리한다(매 프레임, 페이즈 무관). 활성 항목 수가 적어 비용은 무시할 수준.
            stats.Prune(Time.time);

            if (actions.Count == 0) return;

            // 페이즈 게이팅을 호스트가 한 곳에서 처리한다 — 액션이 각자 DayNightManager를 폴링하면
            // 규칙이 갈라진다(WL-044). 액션은 ActivePhase로 자기 요구만 선언한다.
            bool isNight = DayNightManager.Instance == null ||
                DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Night;

            // 웨이브 종료(밤→낮) 경계를 호스트가 감지해 액션에 통지한다 — "이 웨이브 동안만" 유효한
            // 상태(성장 스택 등)를 일괄 초기화하는 지점이다(#300).
            //
            // 이벤트(DayNightManager.OnNightToDay)를 구독하지 않고 위에서 이미 읽은 값의 전이를 보는 이유:
            //  ① 신호원이 하나로 유지된다 — 페이즈 게이팅과 같은 값을 쓰므로 "게이팅은 밤인데 통지는 안 온다"
            //     같은 어긋남이 구조적으로 불가능하다(WL-044가 지적한 갈라짐).
            //  ② 초기화 순서에 안전하다 — `Instance`가 아직 null인 시점에 조립된 타워(씬 선배치·테스트 씬)도
            //     구독을 놓치지 않는다. 이벤트 구독이면 그 타워만 영구히 통지를 못 받는 무증상 실패가 된다.
            //  ③ 구독 해제가 필요 없다 — 파괴된 타워가 매니저의 이벤트 목록에 남을 경로가 없다.
            //
            // `wasNight` 초기값 false는 안전하다: 밤에 조립된 타워는 첫 프레임에 false→true 전이만 만들고
            // 그 방향으로는 통지하지 않는다.
            if (wasNight && !isNight)
            {
                for (int i = 0; i < actions.Count; i++) actions[i]?.OnWaveEnd();
            }
            wasNight = isNight;

            float deltaTime = Time.deltaTime;
            for (int i = 0; i < actions.Count; i++)
            {
                TowerAction action = actions[i];
                if (action == null) continue;   // [SerializeReference] 클래스 rename 시 null 항목이 생긴다
                if (action.ActivePhase == TowerActivePhase.NightOnly && !isNight) continue;

                action.Tick(deltaTime);
            }
        }

        // IAttacker 계약. 공격 액션이 없으면 false — 오라 전용 타워가 단일 대상 공격 경로에 끌려가지 않는다.
        public bool TryAttack(IDamageable target) => Get<AttackAction>()?.TryAttack(target) ?? false;

#if UNITY_EDITOR
        // 액션이 MonoBehaviour가 아니게 되면서 각자의 OnDrawGizmosSelected를 잃었다 — 호스트가 대신 부른다.
        void OnDrawGizmosSelected()
        {
            if (actions == null) return;
            for (int i = 0; i < actions.Count; i++) actions[i]?.DrawGizmos();
        }
#endif
    }
}