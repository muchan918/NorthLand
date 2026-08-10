using System;
using NorthLand.Core;
using UnityEngine;

namespace NorthLand.Combat
{
    public class Enemy : MonoBehaviour, IAttacker, IDamageable
    {
        [SerializeField] EnemyAsset data;
        [SerializeField] Transform hitPosition;

        public Transform HitPosition => hitPosition;
        // TODO(TBD): 대상 탐지 필터링을 LayerMask로 할지 Tag로 할지 미확정.
        //            현재는 임시로 LayerMask 방식 사용. 팀 컨벤션 회의 후 결정 및 수정 예정.
        [SerializeField] LayerMask targetLayerMask;   // 아군 유닛 + 본진 레이어

        float currentHp;
        float cooldownTimer;
        bool isDying;

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

            currentHp = Stat != null ? Stat.MaxHp : 0f;

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
            if (data != null && data.EnemyType == EnemyType.Boss && data.Boss != null && data.Boss.BehaviorTree != null)
            {
                behaviorAgent = GetComponent<Unity.Behavior.BehaviorGraphAgent>();
                if (behaviorAgent == null)
                {
                    behaviorAgent = gameObject.AddComponent<Unity.Behavior.BehaviorGraphAgent>();
                }

                behaviorAgent.Graph = data.Boss.BehaviorTree;
            }
        }

        public Faction Faction => Faction.Enemy;
        public bool IsDead => currentHp <= 0f;

        // HP UI(월드 스페이스 체력바 등)가 구독하는 공개 계약. Awake와 TakeDamage에서 통지.
        public float CurrentHp => currentHp;
        public float MaxHp => Stat != null ? Stat.MaxHp : 0f;
        public event Action<float, float> OnHpChanged;

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
                Debug.Log($"[처형] {name}#{GetInstanceID()}: HP {currentHp:F1}/{MaxHp:F1} " +
                          $"(임계 {executeThreshold:P0}) → 집행", this);

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

            if (!hasTarget || cooldownTimer > 0f)
            {
                return;
            }

            if (TryAttack(target))
            {
                cooldownTimer = AttackInterval;
            }
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
        // 사망 처리. 추후 오브젝트 풀링 도입 시 이 메서드 내부만 "풀 반환"으로 교체하면 된다.
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

            // 사망 연출 지연 동안(파괴 전까지) 보스 BT가 계속 돌지 않도록 에이전트를 끈다.
            if (behaviorAgent != null)
            {
                behaviorAgent.enabled = false;
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

        // 사거리 내에서 가장 가까운 아군 대상(유닛/본진)을 타겟으로 선정
        IDamageable FindTarget()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, AttackRange, hitBuffer, targetLayerMask);

            IDamageable closest = null;
            float closestSqrDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                var damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable != null
                    && damageable.Faction != Faction
                    && !damageable.IsDead)
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
