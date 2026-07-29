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

        // 이 타워가 "무엇을 하는 물건인지"를 담는 행동들. SO를 보고 Build에서 조립된다.
        // 단일 참조가 아니라 리스트인 이유: `if (attack != null)` 분기가 누적돼 이 클래스가 다시
        // 만능 클래스로 돌아가는 것을 구조적으로 막고, 공격+오라 하이브리드 타워를 공짜로 허용한다.
        readonly List<ITowerBehaviour> behaviours = new List<ITowerBehaviour>();

        // 조립 여부. Build를 두 번 타지 않게 하고, OnEnable 폴백 경로의 판단 근거가 된다.
        bool built;

        /// 이 타워가 해당 행동을 가지는지. 소비처가 타워의 **구상 타입**이 아니라 **능력**을 묻게 하는 창구다
        /// (예: 보스 마력 봉인은 `Has&lt;AttackBehaviour&gt;()`인 타워만 대상으로 한다).
        public bool Has<T>() where T : class, ITowerBehaviour
        {
            for (int i = 0; i < behaviours.Count; i++)
            {
                if (behaviours[i] is T) return true;
            }
            return false;
        }

        /// 해당 행동을 반환한다. 없으면 null.
        public T Get<T>() where T : class, ITowerBehaviour
        {
            for (int i = 0; i < behaviours.Count; i++)
            {
                if (behaviours[i] is T match) return match;
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

            ReinitializeBehaviours();   // 풀 재사용 등으로 다시 켜진 경우 행동을 재무장
            Register();
        }

        // 발사 시점 통지(탄약 시각 연출 등 구독용 — 예: 캐논 포탄이 발사 순간 사라짐).
        public event Action OnFired;

        // 발사 통지 발행 창구. 행동(AttackBehaviour)이 실제 발사 주체지만, 구독자(TowerReloadVisual)는
        // 타워를 보고 붙으므로 이벤트 소유는 호스트에 남긴다.
        internal void RaiseFired() => OnFired?.Invoke();

        void OnDisable()
        {
            // 행동이 외부에 남긴 상태(버프 오라가 다른 타워에 부여한 modifier 등)를 먼저 걷어낸다.
            // Unregister보다 앞이어야 한다 — 오라가 대상 목록을 다시 계산할 때 자기 자신이 아직
            // 목록에 있어도 무해하지만, 반대 순서면 통지를 받은 오라가 이미 사라진 상태를 볼 수 있다.
            for (int i = 0; i < behaviours.Count; i++) behaviours[i].Dispose();

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
                ReinitializeBehaviours();
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

            // 재조립이면 이전 행동이 외부에 남긴 상태를 먼저 걷어낸다.
            for (int i = 0; i < behaviours.Count; i++) behaviours[i].Dispose();

            data = asset;
            TowerBehaviourFactory.Create(gameObject, asset, behaviours);
            built = true;

            // 새 SO에서 빠진 행동 컴포넌트를 제거한다. 남겨도 호스트가 Tick하지 않아 무동작이지만,
            // Inspector에 "동작하지 않는 행동"이 붙어 있으면 디버깅할 때 사실과 다르게 읽힌다.
            StripUnusedBehaviourComponents();

            ReinitializeBehaviours();
            Register();
        }

        // 조립 결과에 포함되지 않은 ITowerBehaviour 컴포넌트를 제거한다(SO가 바뀐 재조립에서만 발생).
        void StripUnusedBehaviourComponents()
        {
            var attached = GetComponents<MonoBehaviour>();
            for (int i = 0; i < attached.Length; i++)
            {
                if (attached[i] is not ITowerBehaviour behaviour) continue;
                if (behaviours.Contains(behaviour)) continue;

                behaviour.Dispose();
                Destroy(attached[i]);
            }
        }

        void ReinitializeBehaviours()
        {
            if (behaviours.Count == 0) return;

            var context = new TowerBuildContext(this, data, enemyLayerMask, firePoint);
            for (int i = 0; i < behaviours.Count; i++) behaviours[i].Initialize(in context);
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

            _rangeCircle.SetRadius(AttackRange);
            _rangeCircle.Show();
        }

        // 정보 패널용 스탯 텍스트. 각 행동이 자기 설명을 만들고 호스트는 붙이기만 한다 —
        // 타워가 "무엇을 하는 물건인지" 알아야 할 이유가 표시 경로에도 없다.
        // 기여할 행동이 하나도 없으면 null(패널은 통계 구간 없이 설명만 표시).
        string BuildStatsText()
        {
            string result = null;

            for (int i = 0; i < behaviours.Count; i++)
            {
                string piece = behaviours[i].DescribeStats();
                if (string.IsNullOrEmpty(piece)) continue;

                result = result == null ? piece : $"{result}\n{piece}";
            }

            return result;
        }

        // ── IAttacker 계약 ────────────────────────────────────────────────
        // 값은 공격 행동이 소유한다(기본값=SO, modifier=원장). 이 타워가 공격하지 않으면 전부 0 —
        // "공격 스탯이 없는 타워"라는 상태를 별도 분기 없이 값으로 표현한다.
        public float AttackDamage => Get<AttackBehaviour>()?.Damage ?? 0f;

        public float AttackRange => Get<AttackBehaviour>()?.Range ?? 0f;

        public float AttackInterval => Get<AttackBehaviour>()?.Interval ?? 0f;

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

            if (behaviours.Count == 0) return;

            // 페이즈 게이팅을 호스트가 한 곳에서 처리한다 — 행동이 각자 DayNightManager를 폴링하면
            // 규칙이 갈라진다(WL-044). 행동은 ActivePhase로 자기 요구만 선언한다.
            bool isNight = DayNightManager.Instance == null ||
                DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Night;

            float deltaTime = Time.deltaTime;
            for (int i = 0; i < behaviours.Count; i++)
            {
                ITowerBehaviour behaviour = behaviours[i];
                if (behaviour.ActivePhase == TowerActivePhase.NightOnly && !isNight) continue;

                behaviour.Tick(deltaTime);
            }
        }

        // IAttacker 계약. 공격 행동이 없으면 false — 오라 전용 타워가 단일 대상 공격 경로에 끌려가지 않는다.
        public bool TryAttack(IDamageable target) => Get<AttackBehaviour>()?.TryAttack(target) ?? false;
    }
}