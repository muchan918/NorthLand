using System;
using NorthLand.Core;
using UnityEngine;

namespace NorthLand.Combat
{
    public class Enemy : MonoBehaviour, IAttacker, IDamageable, ITargetProfile
    {
        [SerializeField] EnemyAsset data;
        [SerializeField] Transform hitPosition;

        public Transform HitPosition => hitPosition;
        public EnemyAsset Asset => data;

        // TODO(TBD): 대상 탐지 필터링을 LayerMask로 할지 Tag로 할지 미확정.
        //            현재는 임시로 LayerMask 방식 사용. 팀 컨벤션 회의 후 결정 및 수정 예정.
        [SerializeField] LayerMask targetLayerMask;   // 아군 유닛 + 본진 레이어

        float currentHp;
        float cooldownTimer;
        bool isDying;

        // 스윙 중 타격까지 남은 시간(#452). 0보다 크면 「이미 휘두르는 중」이다.
        // 쿨다운(다음 스윙까지)과는 별개의 축이다 — 둘을 한 타이머로 겸하면 와인드업이 공격 간격에
        // 더해져 실효 간격이 늘어난다.
        float swingHitTimer;

        // 공격 모션은 `monsterStateMachine`을 경유해 지시한다(#452) — `MonsterAnimation`을 직접 들지
        // 않는다. 애니메이터 기록 주체와 참조 해석을 그쪽 한 컴포넌트로 모으기 위함이며,
        // 근거는 `MonsterStateMachine.RequestAttackSwing`의 주석에 있다.

        /// 이 적이 **처치되어** 사라질 때 정확히 1회 발행된다. 첫 인자는 마지막으로 피해를 준 주체
        /// (모르면 null). 킬스택 성장 타워(#300)가 처치 귀속을 아는 유일한 창구다.
        ///
        /// 왜 여기인가: 귀속을 알 수 있는 지점은 `TakeDamage`(소스가 실려 온다)뿐이고, 사망 확정은
        /// `Die()`의 `isDying` 게이트뿐이다. 둘을 잇는 자리가 여기 하나다.
        /// `Projectile.DamageDealt`와 같은 static 이벤트 idiom을 쓴다.
        ///
        /// ⚠ **경로 이탈(본진 도달)은 발행되지 않는다** — `HandleRouteCompleted`가 `Die()`를 거치지
        /// 않고 바로 Destroy하기 때문이다. "죽인 것"과 "놓친 것"이 구조적으로 갈린다.
        /// ⚠ static이므로 구독자는 **반드시 해제**할 것(죽은 구독자가 남으면 파괴된 타워를 계속 건드린다).
        public static event Action<IAttacker, Enemy> Killed;

        /// 이 적이 씬에 등장한 직후 **`Start`에서** 1회 발행된다. 몬스터 체력바(#447)가 프리팹 종속
        /// 없이 자신을 붙이는 창구이며, `PlayerBase.OnBaseSpawned`와 같은 idiom이다.
        ///
        /// **`Awake`가 아니라 `Start`인 이유가 셋이다.**
        /// ① `MonsterSpawn`이 `Instantiate` **직후 동기로** `ApplyWaveHpScale`을 부르므로(`SpawnPrefab`),
        ///    `Start` 시점의 `MaxHp`는 **웨이브 배율이 반영된 확정값**이다 — 구독자가 "지금 읽은 최대치는
        ///    임시일 수 있다"를 알아야 하는 부채가 사라진다.
        /// ② 구독자가 `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`로 붙는데, 그 시점은 **첫 로드 씬의
        ///    모든 `Awake` 이후 · `Start` 이전**이다. `Awake`에서 발행하면 첫 씬에 미리 놓여 있던 적은
        ///    구독자가 0인 상태로 신호를 흘린다.
        /// ③ `SpawnPrefab`의 검증 실패 경로는 `Instantiate` 뒤 `Destroy`로 끝나는데, 그 경우 `Start`가
        ///    아예 돌지 않아 **한 프레임짜리 유령 구독**이 생기지 않는다.
        ///
        /// ⚠ static이므로 구독자는 반드시 해제할 것.
        public static event Action<Enemy> Spawned;

        // 마지막으로 피해를 준 주체. DoT는 StatusEffectHandler가 원 소스를 그대로 실어 보내므로
        // 화상·독으로 죽은 적도 그 타워에 귀속된다. 스킬·환경 피해는 소스가 null이라 귀속되지 않는데,
        // 이것이 **의도**다 — 마지막 일격이 스킬이면 그 처치는 어느 타워의 것도 아니다.
        IAttacker lastDamageSource;

        // 원거리 공격의 비행 부품(#274 Phase 4.5). 발사마다 만들지 않고 인스턴스별 1회 생성해 재사용한다 —
        // 부품은 무상태라 이 적이 쏜 투사체들이 함께 참조해도 안전하다(진행값은 Projectile이 소유).
        HomingFlight rangedFlight;

        // 받는 피해 배수(#233). 1=그대로, 0=무적, 1 초과=취약. 방어 태세 패턴이 감소치를 건다.
        // 이 값을 Enemy가 소유하는 이유: 피해 적용 지점이 TakeDamage 하나뿐이라
        // EnemyAgent가 같은 값을 들면 동기화가 깨진다(EnemyAgent는 전달만 한다).
        float damageTakenFactor = 1f;

        // 처형 표식(#318). 감전 보상 「처형」이 걸고, 지속 동안 어떤 피해원으로든 체력이 임계 이하로
        // 떨어지면 그 순간 집행된다. 표식을 Enemy가 소유하는 이유는 damageTakenFactor와 같다 —
        // 피해 적용 지점이 TakeDamage 하나뿐이라 다른 데서 판정하면 동기화가 깨진다.
        //
        // StatusEffectHandler에 얹지 않은 이유는 "핸들러가 도트 전용이라서"가 아니다 —
        // ApplySlow(감속·스턴)가 이미 피해가 아닌 시간제한 전투 판정을 소유·갱신·만료시킨다.
        // 실제 이유는 조회 비용이다: 핸들러는 필요할 때 AddComponent로 붙는 컴포넌트라
        // TakeDamage가 매 피해마다 GetComponent + null 가드를 하게 된다. 표식은 모든 피해가
        // 통과하는 이 경로에서 읽혀야 하므로 필드로 직접 든다.
        //
        // ⚠ 판정 수정자가 늘어나면(최소 피해 보장·마지막 일격 무효 등) 이 방식은 한계가 온다.
        // damageTakenFactor 하나였던 것이 #318로 4개가 됐다 — 축이 더 늘면 상태 컨테이너로 승격할 것.
        float executeThreshold;       // MaxHp 대비 비율. 0 = 표식 없음
        float executeMarkRemaining;   // 남은 표식 시간(초)
        bool  executeDebugLog;        // 집행 순간 로그(검증용). 부여한 ExecuteEffect의 플래그를 그대로 받는다

        // 이동 액추에이터(선택적). 대상이 사거리에 들면 멈추도록 이 컴포넌트가 구동한다.
        // 구체 타입이 아니라 계약(IMovementAgent)에 의존 — 이동 구현에 결합하지 않는다.
        IMovementAgent movement;

        // 타겟 탐색용 재사용 버퍼. 매 프레임 힙 할당을 피하기 위해 사용(최대 16개 감지).
        readonly Collider[] hitBuffer = new Collider[16];

        private MonsterStateMachine monsterStateMachine;

        private IRouteMovementAgent routeMovement;

        public MovementMode MovementMode => data != null ? data.MovementMode : global::MovementMode.Ground;

        // 보스 BehaviorTree 실행 주체(보스가 아니면 null). 정지 핸들 확보용 필드 —
        // 게임 종료·사망 시 이 에이전트를 꺼서 그래프 틱을 멈춘다.
        private Unity.Behavior.BehaviorGraphAgent behaviorAgent;

        // EnemyType에 맞는 공통 전투 스탯 해석. data 미할당 시 null.
        EnemyAsset.CombatFields Stat => data == null ? null : data.EnemyType switch
        {
            EnemyType.Melee  => data.Melee.Stat,
            EnemyType.Ranged => data.Ranged.Stat,
            EnemyType.Boss   => data.Boss.Stat,
            _ => null,
        };

        void Awake()
        {
            monsterStateMachine = GetComponent<MonsterStateMachine>();

            // MonsterAnimation 미발견 경고는 MonsterStateMachine.Awake가 낸다 — 참조를 해석하는
            // 컴포넌트가 경고도 낸다(경고를 두 곳에서 내면 같은 사실이 두 줄로 찍힌다).

            // 배율 미적용 상태의 최대치다. 스포너가 곧 ApplyWaveHpScale로 덮어쓴다 —
            // 스포너를 거치지 않는 경로(테스트 씬 직접 배치)는 배율 1이라 이 값이 그대로 정답이 된다.
            currentHp = MaxHp;

            // 자식까지 탐색(WL-093): MonsterMove가 자식 오브젝트에 붙는 프리팹에서도 movement를 찾도록
            // GetComponentInChildren 사용 — line 61·MonsterSpawn·MonsterStateMachine의 탐색 범위와 일치시킨다.
            movement = GetComponentInChildren<IMovementAgent>();

            // 기준 속도만 주입한다. 패턴 배수·디버프 배수는 movement가 축별로 소유하고 합성한다(#233).
            if (movement != null && Stat != null)
            {
                movement.SetMoveSpeed(Stat.MoveSpeed);
            }
            else if (movement == null)
            {
                // 이동 액추에이터를 못 찾으면 보스 속도 가감속 패턴이 조용히 무동작하므로 경고로 드러낸다.
                Debug.LogWarning($"[{name}] IMovementAgent를 찾지 못해 이동속도 패턴이 동작하지 않습니다.", this);
            }
            if (hitPosition == null)
            {
                hitPosition = transform;
                Debug.LogError($"[Enemy] {name}: hitPosition 미할당 — 피벗으로 폴백. 프리팹에 몸통 트랜스폼을 지정하라.", this);
            }
            OnHpChanged?.Invoke(currentHp, MaxHp);

            routeMovement = GetComponentInChildren<IRouteMovementAgent>();

            if (routeMovement != null)
            {
                routeMovement.RouteCompleted += HandleRouteCompleted;
            }

            // 보스 데이터 주도 AI: EnemyAsset.Boss.BehaviorTree에 그래프가 지정돼 있으면
            // BehaviorGraphAgent를 확보(없으면 부착)해 그래프를 주입한다. 그래프 실행 주체는 에이전트지만,
            // "어떤 보스가 어떤 그래프를 쓰는지"는 프리팹 배선이 아니라 SO(tracked)가 단일 출처로 소유한다.
            if (IsBoss && data.Boss != null && data.Boss.BehaviorTree != null)
            {
                behaviorAgent = GetComponent<Unity.Behavior.BehaviorGraphAgent>();
                if (behaviorAgent == null)
                {
                    behaviorAgent = gameObject.AddComponent<Unity.Behavior.BehaviorGraphAgent>();
                }

                behaviorAgent.Graph = data.Boss.BehaviorTree;
            }
        }

        void Start()
        {
            // 체력바 부착 창구(#447). 프리팹에 UI를 심는 대신 이 신호를 UI 레이어가 받아 붙인다.
            // Awake가 아니라 여기인 이유는 Spawned 선언부의 ①②③.
            Spawned?.Invoke(this);
        }

        public Faction Faction => Faction.Enemy;
        public bool IsDead => currentHp <= 0f;

        // HP UI(월드 스페이스 체력바 등)가 구독하는 공개 계약. Awake와 TakeDamage에서 통지.
        public float CurrentHp => currentHp;

        // 최대 HP는 SO 값 × 웨이브 배율이다. **배율을 여기 한 곳에만 곱한다** —
        // HpRatio·executeThreshold(처형 표식)·Heal·HP UI가 전부 이 프로퍼티를 경유하므로
        // 곱셈을 분산시키면 그중 하나를 빠뜨렸을 때 조용히 어긋난다.
        public float MaxHp => Stat != null ? Stat.MaxHp * hpScale : 0f;

        /// ITargetProfile — 타워 조준 정책이 읽는 경로 진행도(#387). `CurrentHp`/`MaxHp`는 위를 그대로 쓴다.
        ///
        /// 경로 이동 컴포넌트가 없으면 **NaN**이다. 0(=종점 도달)으로 대신하면 경로가 없다는 이유만으로
        /// "앞선 적" 조준에 항상 1순위로 뽑힌다 — 모른다는 사실이 값으로 위장되는 자리다.
        public float RemainingRouteDistance =>
            routeMovement != null ? routeMovement.RemainingRouteDistance : float.NaN;

        public event Action<float, float> OnHpChanged;

        // 웨이브 진행에 따른 최대 HP 배율(Docs/Core/CombatBalance.md §4.7).
        // **직렬화하지 않는다** — 프리팹의 성질이 아니라 "이번 스폰이 몇 번째 웨이브인가"의 성질이다.
        // 1 = 무보정이므로 스포너를 거치지 않는 경로(테스트 씬에 직접 배치 등)도 그대로 동작한다.
        float hpScale = 1f;

        /// 스폰 직후 웨이브 HP 배율을 주입한다. **`MonsterSpawn`이 유일한 호출자**다.
        ///
        /// ⚠ **현재 HP를 새 최대치로 다시 채운다.** `Awake`가 이미 `currentHp`를 배율 없는 값으로
        /// 초기화해 버렸기 때문이다(`Instantiate`가 `Awake`를 즉시 돌리므로 스포너가 끼어들 틈이 없다).
        /// 그래서 이 메서드는 **피해를 입기 전에 한 번만** 부르는 것을 전제로 한다 — 전투 중에 부르면
        /// 체력이 회복된다. (풀링이 들어오면 HP 초기화가 `OnEnable`로 옮겨가므로 그때 이 자리도 함께 본다)
        public void ApplyWaveHpScale(float scale)
        {
            if (scale <= 0f)
            {
                Debug.LogError($"[Enemy] {name}: 웨이브 HP 배율이 0 이하입니다({scale}) — 무보정(1)으로 처리합니다.", this);
                scale = 1f;
            }

            hpScale = scale;
            currentHp = MaxHp;
            OnHpChanged?.Invoke(currentHp, MaxHp);
        }

        // Stat 미설정(Stat==null)에서도 안전하도록 null 가드(공개 IAttacker 계약).
        public float AttackDamage => Stat != null ? Stat.AttackDamage : 0f;
        public float AttackRange => Stat != null ? Stat.AttackRange : 0f;
        public float AttackInterval => Stat != null ? Stat.AttackInterval : 0f;

        // ── 보스 BehaviorTree 패턴 훅(#193) ─────────────────────────────
        // 커스텀 BT 노드(CombatSystem/Enemy/Boss)가 호출하는 공개 계약.
        // 숫자(임계값·회복량·배수)는 그래프 노드 입력으로 authoring하고, 여기선 상태 변경만 담당한다.

        // 현재 체력 비율(0~1). "HP 30% 이하" 같은 조건 노드가 참조한다. MaxHp==0이면 0.
        public float HpRatio => MaxHp > 0f ? currentHp / MaxHp : 0f;

        // 보스 판정(#318). EnemyType.Boss는 최종보스(ogre_king)와 중간보스(tank)를 모두 포함한다.
        public bool IsBoss => data != null && data.EnemyType == EnemyType.Boss;

        /// 자폭병 술어(#453). 켜져 있으면 이 적은 **본진만 조준하고 닿는 순간 터진다**.
        /// 저작은 `EnemyAsset.SelfDestruct`이며 EnemyType과는 직교한다(현재 자폭병도 Melee 스탯 블록을 쓴다).
        public bool IsSuicideBomber => data != null && data.SelfDestruct != null && data.SelfDestruct.Enabled;

        // 체력 회복. MaxHp를 넘지 않도록 클램프하고, 이미 죽었으면 무시. HP UI에 변경을 통지한다.
        public void Heal(float amount)
        {
            if (isDying || amount <= 0f || Stat == null)
            {
                return;
            }

            currentHp = Mathf.Min(currentHp + amount, MaxHp);
            OnHpChanged?.Invoke(currentHp, MaxHp);
        }

        // 현재 이동속도 배수(가감속 램프 노드가 보간 시작값으로 읽는다).
        // #233 이후 값의 소유자는 movement의 패턴 축이다 — 여기서 별도로 들지 않는다.
        public float SpeedMultiplier => movement != null ? movement.PatternSpeedFactor : 1f;

        // 이동속도 배수 설정(가감속 패턴). #233 이후 movement의 패턴 축에 위임한다 —
        // 감속 타워의 디버프 축과 곱해져야 서로를 지우지 않으므로 여기서 최종 속도를 계산하지 않는다.
        // 중간보스 그래프(MidBossBehavior.asset)가 이 진입점을 쓰고 있어 남겨둔다.
        // 신규 노드는 EnemyAgent.PatternSpeedFactor를 경유한다.
        public void SetSpeedMultiplier(float multiplier)
        {
            if (movement == null)
            {
                return;
            }

            movement.PatternSpeedFactor = multiplier;
        }

        // ── 보스 BT 진입점(#233) ─────────────────────────────

        // BT 이동 소유권. 켜져 있는 동안 Update가 이동·타겟 통지를 건드리지 않으므로
        // 노드가 정지와 전진을 직접 지시할 수 있다(P1 준비 동작 중 정지, 돌진 중 전진 유지).
        //
        // IsStopped 뿐 아니라 타겟 통지까지 함께 다루는 이유: MonsterStateMachine이
        // Attack 상태에서 SetMoveEnabled(false)를 걸기 때문에(MonsterStateMachine.cs:141),
        // 타겟 통지를 살려두면 돌진이 본진 사거리에 진입하는 순간 멈춰 P1이 절름발이가 된다.
        //
        // 통지를 "막는 것"만으로는 부족하다. MonsterMove에는 IsStopped(:147)와 canMove(:159)
        // 두 개의 독립된 게이트가 있고 노드는 IsStopped만 만진다. 소유권 획득 전에 이미
        // hasTarget=true(Attack 상태, canMove=false)였다면 통지만 막아도 그 상태가 그대로 latch되어
        // 돌진 노드가 IsStopped=false를 매 프레임 써도 보스가 제자리에서 배수만 올린다.
        // 그래서 Update가 소유권 브랜치에서 SetHasTarget(false)로 상태를 내려준다.
        //
        // 부수 효과로 소유권 중에는 근접 평타가 나가지 않는다 — 충돌 피해가 그 역할을 대신한다.
        public bool MovementOwnedByBehavior { get; set; }

        // 받는 피해 배수. 0 미만은 클램프한다(0=무적). 상한은 두지 않아 취약 디버프로도 쓸 수 있다.
        public float DamageTakenFactor
        {
            get => damageTakenFactor;
            set => damageTakenFactor = Mathf.Max(0f, value);
        }

        // 처형 표식 부여(#318). 재적용은 갱신이다(임계·지속 모두 덮어쓴다) —
        // StatusEffectHandler.ApplyOrRefresh와 같은 semantics.
        // 보스 제외 가드는 여기가 아니라 호출부(ExecuteEffect)에 둔다: TakeDamage 경로에
        // 처형과 무관한 조건을 심지 않기 위함이다.
        public void MarkForExecute(float thresholdRatio, float duration, bool debugLog = false)
        {
            if (duration <= 0f) return;

            executeThreshold = thresholdRatio;
            executeMarkRemaining = duration;
            executeDebugLog = debugLog;

            // 표식을 건 타격 자체도 집행 대상이다. SkillManager.CastAt이 데미지를 먼저 적용하고
            // 표식을 나중에 걸기 때문에(SkillManager.cs:195-196), 여기서 한 번 검사하지 않으면
            // "감전으로 임계 아래까지 깎았는데 처형이 안 되는" 구멍이 생긴다.
            // TakeDamage 밖이라 사망 처리를 여기서 직접 이어줘야 한다.
            if (TryExecute())
            {
                OnHpChanged?.Invoke(currentHp, MaxHp);
                Die();
            }
        }

        // 처형 판정 1회(#318). 표식이 살아 있고 체력 비율이 임계 이하면 HP를 0으로 확정한다.
        // 사망 처리(Die)는 호출부가 이어서 한다 — TakeDamage는 기존 `if (IsDead) Die();`가,
        // MarkForExecute는 직접 호출한다. 신규 사망 경로를 만들지 않아 isDying 이중 킬 방어가
        // 그대로 작동한다.
        bool TryExecute()
        {
            if (IsDead || executeMarkRemaining <= 0f || HpRatio > executeThreshold)
            {
                return false;
            }

            if (executeDebugLog)
                // P0 서식을 쓰지 않는다 — SkillStatsFormatter가 금지한 것과 같은 이유
                // (ko-KR PercentPositivePattern이 "10 %"로 공백을 넣고 CurrentCulture 의존).
                Debug.Log($"[처형] {name}#{GetInstanceID()}: HP {currentHp:F1}/{MaxHp:F1} " +
                          $"(임계 {executeThreshold * 100f:0.#}%) → 집행", this);

            currentHp = 0f;
            executeMarkRemaining = 0f;
            executeThreshold = 0f;
            return true;
        }

        void Update()
        {
            if (Stat == null || isDying)
            {
                return;
            }

            // 표식 소진(#318). 스턴·BT 소유권 분기보다 앞에 두는 이유: 그 분기들은 return으로 빠져나가고,
            // 표식은 적의 행동 상태와 무관하게 실시간으로 만료돼야 한다.
            if (executeMarkRemaining > 0f)
            {
                executeMarkRemaining -= Time.deltaTime;
                if (executeMarkRemaining <= 0f) executeThreshold = 0f;
            }

            GameManager gameManager = GameManager.Instance;

            if (gameManager != null &&gameManager.Result != GameResult.Playing)
            {
                if (movement != null)
                {
                    movement.IsStopped = true;
                }

                // 게임 종료(승리/패배) 후 보스 BT가 계속 tick하지 않도록 에이전트를 끈다.
                if (behaviorAgent != null)
                {
                    behaviorAgent.enabled = false;
                }

                monsterStateMachine?.SetHasTarget(false);
                CancelSwing();
                return;
            }

            // BT가 이동을 소유한 동안은 이동·공격에서 손을 뗀다(MovementOwnedByBehavior 주석 참조).
            //
            // 타겟 없음을 한 번 내려주는 것이 핵심이다. 소유권 진입 전에 남아 있던 Attack 상태를
            // 풀지 않으면 MonsterMove.canMove가 false로 고착돼 돌진 노드가 보스를 움직일 수 없다.
            // ChangeState가 동일 상태를 걸러내므로 매 프레임 호출해도 무해하다.
            //
            // 쿨다운은 계속 흘려보낸다 — 소유권 반납 직후 인위적인 공격 지연이 생기지 않게.
            if (MovementOwnedByBehavior)
            {
                monsterStateMachine?.SetHasTarget(false);
                CancelSwing();
                cooldownTimer -= Time.deltaTime;
                return;
            }

            // 스턴은 이동뿐 아니라 공격도 막는다(#164). 이동만 막으면 본진·병사에 붙어 때리는
            // 몬스터에게는 효과가 0이다 — 아래 :IsStopped 대입이 이미 정지를 세워둔 상태라
            // 관측 가능한 변화가 없다. 성문 앞 난전이 소다 타워를 사는 이유인데 거기서만
            // 안 먹히는 결과가 된다.
            //
            // 타겟 없음을 내려주는 이유: MonsterStateMachine이 hasTarget을 먼저 평가하므로
            // 통지를 남겨두면 스턴 중에도 공격 모션이 계속 재생된다.
            //
            // 쿨다운은 계속 흘려보낸다 — 소유권 브랜치와 같은 규칙. 스턴 지속시간이 곧 효과이고,
            // 여기서 쿨다운까지 얼리면 스턴 해제 후 눈에 안 보이는 추가 지연이 붙는다.
            if (movement != null && movement.IsStunned)
            {
                monsterStateMachine?.SetHasTarget(false);
                CancelSwing();
                cooldownTimer -= Time.deltaTime;
                return;
            }

            IDamageable target = FindTarget();
            bool hasTarget = target != null;

            if (movement != null)
            {
                movement.IsStopped = hasTarget;
            }

            monsterStateMachine?.SetHasTarget(hasTarget);

            cooldownTimer -= Time.deltaTime;

            // 스윙 중이면 타격 시점만 기다린다(#452). 와인드업 사이에 대상이 죽거나 사거리에서
            // 벗어나면 **헛스윙**이 된다 — 의도한 거동이다. 모션은 이미 나갔으므로 피해만 빈다.
            if (swingHitTimer > 0f)
            {
                swingHitTimer -= Time.deltaTime;

                if (swingHitTimer <= 0f)
                {
                    swingHitTimer = 0f;

                    if (hasTarget)
                    {
                        TryAttack(target);
                    }
                }

                return;
            }

            if (!hasTarget || cooldownTimer > 0f)
            {
                return;
            }

            // 쿨다운을 **스윙 시작 기준**으로 재는 것이 핵심이다(#452). 타격 기준으로 재면 실효 공격
            // 간격이 `간격 + 와인드업`으로 늘어나 밸런싱 수치가 조용히 어긋나고, 애니메이션 1주기와
            // 공격 1주기가 다시 갈라진다.
            //
            // 성공 여부와 무관하게 재는 것도 의도다. 예전에는 실패 시 매 프레임 재시도했는데,
            // 실패 사유는 투사체 프리팹 미지정 같은 저작 누락이라 재시도로 낫는 종류가 아니다.
            cooldownTimer = AttackInterval;

            float windup = monsterStateMachine != null
                ? monsterStateMachine.RequestAttackSwing(AttackInterval)
                : 0f;

            if (windup > 0f)
            {
                swingHitTimer = windup;
                return;
            }

            // 스윙 제어가 없는 프리팹(공격 클립 미지정 — 자폭병·Phantom)은 예전 즉발 경로를 그대로 쓴다.
            TryAttack(target);
        }

        // 예약된 타격을 접는다(#452). 스턴·BT 소유권 진입·게임 종료에서 부른다.
        //
        // 접지 않으면 **스턴 중에 예약된 피해가 스턴이 풀린 뒤 그대로 들어간다** — #164가 막으려던
        // 바로 그 구멍이 와인드업만큼 시간차를 두고 되살아난다. 증상이 "스턴을 걸었는데 본진이
        // 깎였다"라 원인에서 멀다.
        void CancelSwing()
        {
            swingHitTimer = 0f;
            monsterStateMachine?.CancelAttackSwing();
        }


        public void TakeDamage(DamageInfo info)
        {
            // 처치 귀속(#300)의 유일한 기록 지점. **조건 없이 덮어쓴다** — "마지막으로 때린 쪽"이
            // 곧 처치자이므로, 소스 없는 피해(스킬·환경)가 마지막이면 귀속도 함께 사라져야 맞다.
            lastDamageSource = info.Source;

            // 받는 피해 배수를 여기 한 곳에서만 적용한다(#233) — 방어 태세 패턴의 감쇠 지점.
            currentHp -= info.Amount * damageTakenFactor;

            // 피격 로그(검증용) — 필요할 때 아래 주석을 풀어 쓴다.
            // 적·소스 양쪽에 InstanceID를 붙이는 이유: 같은 프리팹에서 나온 개체는 이름이 전부 같아
            // (Yellow_Grummy(Clone) 2마리, FlameRollyShooter(Clone) 2기) 로그가 뒤섞이면 못 가른다.
            // 소스 ID는 곧 상태이상 중첩 키(HitEffect.SourceKey의 baseId)라 중첩 판정의 근거이기도 하다.
            // Component src = info.Source as Component;
            // Debug.Log($"[HP] {name}#{GetInstanceID()}: -{info.Amount * damageTakenFactor:F1} " +
            //           $"(from {(src != null ? $"{src.name}#{src.GetInstanceID()}" : "?")}) " +
            //           $"→ {currentHp:F1}/{MaxHp:F1}");

            TryExecute();   // 처형 판정(#318). 아래 `if (IsDead) Die();`가 사망 처리를 이어받는다.

            OnHpChanged?.Invoke(currentHp, MaxHp);

            if (IsDead)
            {
                Die();
            }
        }

        // 같은 프레임 다중 타격에 의한 이중 사망 처리 방지
        // 처치 사망. 자폭 사망은 SelfDestruct(#453)이며 Killed 발행 여부만 다르다.
        void Die()
        {
            if (isDying)
            {
                return;
            }

            isDying = true;

            // 처치 통지는 여기서 1회만 나간다 — 위 isDying 게이트 뒤라 같은 프레임 다중 타격에도
            // 중복 발행되지 않는다. 사망 연출(destroyDelay)보다 앞이라 통지가 지연되지도 않는다.
            Killed?.Invoke(lastDamageSource, this);

            BeginDeathSequence();
        }

        /// 자폭 실행(#453). 본진에 확정 피해를 주고 스스로 죽는다.
        ///
        /// 피해량은 `SelfDestruct.Damage` 그대로다 — **웨이브 HP 배율(`hpScale`)을 곱하지 않는다.**
        /// 배율은 최대 HP 한 축에만 걸리는 값이고(§4.7), 자폭 피해까지 함께 커지면 웨이브당
        /// 자폭 총량 ≤ 본진 HP×0.5로 잡아둔 규약 ④의 예산이 후반 웨이브에서 넘친다.
        ///
        /// `DamageInfo.Source`는 `this`다 — 실제로 이 적이 가한 피해이고, `PlayerBase.TakeDamage`는
        /// 소스를 읽지 않으므로 부작용이 없다(보스 P1 충돌 피해가 `null`을 넘기는 것은 그쪽이
        /// 반격·처치 기여 집계 대상에서 빠져야 하기 때문이라 사정이 다르다).
        void Detonate(IDamageable target)
        {
            SpawnExplosionVfx();
            PlayExplosionSfx();

            target.TakeDamage(new DamageInfo(data.SelfDestruct.Damage, this));

            SelfDestruct();
        }

        /// 자폭 폭발음(#452). `AudioManager`의 2D 원샷을 쓴다 —
        /// `SkillManager`의 `PlayClipAtPoint`(볼륨 제어 밖, `Docs/Core/AudioManager.md` §2)를 따라가지 않는다.
        ///
        /// 자기 `AudioSource`를 달지 않는 이유: 자폭병은 같은 프레임에 제거되므로 소스가 함께 죽어
        /// 소리가 첫 프레임에 끊긴다. 파티클을 부모 없이 스폰하는 것과 같은 사정이고, 매니저의
        /// 소스는 씬을 넘어 살아 있으므로 이 축이 아예 없다.
        void PlayExplosionSfx()
        {
            AudioClip clip = data.SelfDestruct.ExplosionSfx;

            // 매니저가 없는 씬(전투 테스트 등)에서는 조용히 넘긴다 — SoundCue와 같은 방침.
            if (clip == null || AudioManager.Instance == null)
            {
                return;
            }

            AudioManager.Instance.PlaySfx(clip, data.SelfDestruct.ExplosionSfxVolume);
        }

        /// 자폭 폭발 파티클(#452).
        ///
        /// **자신의 자식으로 두지 않는다.** 자폭병은 같은 프레임에 제거되므로 자식으로 붙이면
        /// 파티클도 함께 파괴되어 아무것도 보이지 않는다 — 증상이 "프리팹을 넣었는데 폭발이
        /// 안 보인다"라 원인에서 멀다. 같은 이유로 위치는 스폰 시점에 값으로 복사된다.
        ///
        /// 스케일은 프리팹 값에 **곱한다**. 파티클은 저작된 크기가 이미 제각각이라
        /// (FX_Bomb_Exp 17, FireSphereBlast 5) 절대값으로 덮으면 저작 의도가 사라진다.
        void SpawnExplosionVfx()
        {
            GameObject prefab = data.SelfDestruct.ExplosionVfx;

            if (prefab == null)
            {
                return;
            }

            GameObject vfx = Instantiate(prefab, hitPosition.position, Quaternion.identity);

            float scale = data.SelfDestruct.ExplosionScale;

            if (scale > 0f)
            {
                vfx.transform.localScale *= scale;
            }

            // 파티클 시스템을 훑어 최대 수명을 자동 계산하지 않는다 —
            // ResidentSpawner.PlayDespawnEffect와 같은 규칙으로 저작값을 쓴다.
            // 실제로 튜닝하는 값은 "얼마나 오래 보일지"이고 그건 저작자가 정하는 편이 낫다.
            Destroy(vfx, data.SelfDestruct.ExplosionLifetime);
        }

        /// 자폭 사망(#453). 연출·디스폰은 `Die`와 같지만 **`Killed`를 발행하지 않는다.**
        ///
        /// 자폭은 플레이어가 "죽인 것"이 아니라 "놓친 것"이다. 발행하면 자폭 직전에 이 적을 때린
        /// 타워(`lastDamageSource`)가 킬스택(#300)을 얻어 **본진을 얻어맞은 대가로 타워가 성장한다.**
        /// 경로 완주(`HandleRouteCompleted`)가 `Die`를 우회하는 것과 같은 갈림이다.
        ///
        /// HP를 0으로 확정하는 이유: `isDying`만 세우면 `IsDead`가 false로 남아 사망 연출
        /// (destroyDelay 2초) 동안 타워 조준과 적 탐색의 `!IsDead` 필터가 이미 터진 적을 계속
        /// 후보로 잡는다 — 증상이 "타워가 허공을 쏜다"라 원인에서 멀다.
        void SelfDestruct()
        {
            if (isDying)
            {
                return;
            }

            isDying = true;

            currentHp = 0f;
            OnHpChanged?.Invoke(currentHp, MaxHp);

            // 폭발 파티클이 있으면 사망 모션을 건너뛴다(#452) — 터진 몸이 2초에 걸쳐 천천히
            // 쓰러지면 "펑 하고 없어진다"가 성립하지 않는다. 파티클이 없으면 예전대로 모션을
            // 재생한다(즉시 사라지면 아무 피드백이 없어 그게 더 나쁘다).
            BeginDeathSequence(playDeathAnimation: data.SelfDestruct.ExplosionVfx == null);
        }

        // 사망 연출 → 디스폰. 처치(`Die`)와 자폭(`SelfDestruct`)이 공유하는 뒤처리다.
        // 두 경로의 유일한 차이는 `Killed` 발행 여부이며, 그 차이만 호출부에 남긴다.
        // 추후 오브젝트 풀링 도입 시 이 메서드 내부만 "풀 반환"으로 교체하면 두 경로가 함께 따라온다.
        void BeginDeathSequence(bool playDeathAnimation = true)
        {
            // 사망 연출 지연 동안(파괴 전까지) 보스 BT가 계속 돌지 않도록 에이전트를 끈다.
            if (behaviorAgent != null)
            {
                behaviorAgent.enabled = false;
            }

            // 자폭 폭발(#452) — 모션 없이 즉시 제거한다. 상태 기계를 Death로 넘기지 않는 이유는
            // destroyDelay(2초)가 그쪽에 있기 때문이다. 여기서 전이시키면 즉시 제거가 안 된다.
            if (!playDeathAnimation)
            {
                Destroy(gameObject);
                return;
            }

            if (monsterStateMachine != null)
            {
                monsterStateMachine.ChangeState(MonsterState.Death);
            }
            else
            {
                Debug.LogWarning($"[{name}] MonsterStateMachine이 없어 사망 애니메이션 없이 즉시 제거합니다.",this);

                Destroy(gameObject);
            }
        }

        public bool TryAttack(IDamageable target)
        {
            if (target == null || target.IsDead) return false;

            // 자폭병(#453)은 평타 경로를 타지 않는다 — 본진에 닿는 순간 1회 확정 피해를 주고 스스로 죽는다.
            //
            // 대상과 사망 상태를 **둘 다** 막는다. 정상 경로에서는 둘 다 도달 불가다(FindTarget이 본진만
            // 후보로 남기고, Update가 isDying에서 먼저 return한다). 그런데 TryAttack은 IAttacker 공개
            // 계약이라 밖에서 임의로 불릴 수 있고, 그때 각각 이렇게 깨진다:
            //  · 대상 미검사 → "병사에게 달려가 터지는 자폭병"(규약 ④의 예산은 본진 피해만 센다)
            //  · 사망 미검사 → Detonate가 피해를 먼저 주고 SelfDestruct에서야 isDying을 보므로
            //                  **이미 터진 자폭병이 본진을 두 번 때린다**
            // 보스 BT가 자폭 패턴을 쓰기 시작하면 그때 실제 호출 경로가 생기므로 두 방어를 같은 층위에 둔다.
            if (IsSuicideBomber)
            {
                if (isDying || !(target is IBaseStructure))
                {
                    return false;
                }

                Detonate(target);
                return true;
            }

            // Ranged는 투사체 발사, 그 외(Melee/Boss)는 근접 즉시 데미지.
            // (#193: Boss의 BehaviorTree는 속도 가감속·HP 회복 등 상위 패턴을 담당하고,
            //  공격 자체는 이 근접 경로를 그대로 사용한다. 원거리 보스가 필요해지면 확장.)
            if (data.EnemyType == EnemyType.Ranged)
                return TryRangedAttack(target);

            target.TakeDamage(new DamageInfo(AttackDamage, this));
            return true;
        }

        // 원거리 공격: Tower와 동일한 Projectile을 단일 명중(Single)으로 발사한다.
        bool TryRangedAttack(IDamageable target)
        {
            var ranged = data.Ranged;
            if (ranged.ProjectilePrefab == null) return false;

            var obj = Instantiate(ranged.ProjectilePrefab, transform.position, Quaternion.identity);
            if (!obj.TryGetComponent<Projectile>(out var projectile))
            {
                Destroy(obj);   // Projectile 컴포넌트가 없으면 스폰물을 제거하고 공격 실패 처리
                return false;
            }

            // 비행은 쏘는 쪽이 정한다(#274). 적은 아직 궤적 저작 필드가 없어 유도탄 직선으로 고정한다 —
            // 현재 모든 EnemyAsset의 Ranged.ProjectilePrefab이 null이라 이 경로 자체가 미사용이다.
            // 원거리 적을 실제로 붙일 때 EnemyAsset.RangedFields에 [SerializeReference] ProjectileFlight를
            // 추가하면 타워와 같은 부품을 그대로 쓴다.
            //
            // 발사마다 new 하지 않고 인스턴스별로 1회 만들어 재사용한다 — 부품은 무상태라 이 적이 쏜
            // 투사체들이 함께 참조해도 안전하다(진행값은 각 Projectile의 FlightState에 있다).
            rangedFlight ??= new HomingFlight { Speed = ranged.ProjectileSpeed, ArcHeight = 0f };

            projectile.Init(target, AttackDamage, this, rangedFlight, ProjectileImpact.MakeSingle());
            return true;
        }

        private void OnDestroy()
        {
            if (routeMovement != null)
            {
                routeMovement.RouteCompleted -= HandleRouteCompleted;
            }
        }
        private void HandleRouteCompleted()
        {
            if (isDying)
            {
                return;
            }

            if (data != null&& data.MovementMode == MovementMode.Flying)
            {
                Debug.LogWarning($"[{name}] 공중 경로 끝까지 본진을 발견하지 못해 제거합니다.",this);
            }

            Destroy(gameObject);
        }

        // 사거리 내에서 가장 가까운 아군 대상(유닛/본진)을 타겟으로 선정.
        //
        // ⚠ **자폭병(#453)은 본진만 후보로 남긴다.** 근거는 하나다: **규약 ④의 자폭 위험 예산은 본진
        // 피해만 센다**(`CombatBalance.md` §4.2). 웨이브당 자폭 총량을 본진 HP의 절반으로 묶어 난이도를
        // 설계했으므로, 자폭이 병사에게도 터지면 그 예산이 세는 곳이 둘로 갈려 상한의 의미가 사라진다.
        // 저작(레이어 마스크)이 아니라 코드로 못 박는 것도 그래서다 — 예산의 전제가 프리팹 인스펙터에서
        // 조용히 뒤집혀선 안 된다.
        //
        // 대가: 병사가 자폭병을 저지하지 못한다. 이것은 의도다 — 자폭병의 해답은 감속·광역이라는
        // §4.2의 설계와 같은 방향이다.
        //
        // 부수 효과로 소프트락도 함께 막힌다: 병사를 후보로 남기면 `Update`가 `IsStopped = hasTarget`으로
        // 자폭병을 병사 앞에 세우는데, 자폭병의 `AttackDamage`는 쓰이지 않는 값(0)이라 병사를 못 죽이고
        // 영원히 멈춰 서고, 그 밤은 `monsterParent.childCount == 0`에 닿지 못한다.
        // ⚠ 이건 **결정의 근거가 아니라 결과**다. 근거로 읽으면 "그럼 자폭 피해를 병사에게도 주면
        // 되지 않나"로 쉽게 뒤집히는데, 그렇게 하면 위의 예산이 깨진다.
        IDamageable FindTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, AttackRange, hitBuffer, targetLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;

            bool baseOnly = IsSuicideBomber;

            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                var damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable != null
                    && damageable.Faction != Faction
                    && !damageable.IsDead
                    && (!baseOnly || damageable is IBaseStructure))
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
    }
}
