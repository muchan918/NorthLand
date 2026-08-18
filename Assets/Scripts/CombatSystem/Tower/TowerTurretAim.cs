using UnityEngine;

namespace NorthLand.Combat
{
    // 포탑 마디를 사거리 안의 적 쪽으로 계속 돌려두는 연출 컴포넌트(#336).
    //
    // `TowerReloadVisual`(발사 시 탄약 모형 숨김)과 같은 축이다 — 전투 판정은 액션이 하고, 이 컴포넌트는
    // **보이는 것만** 맞춘다. 붙이지 않아도, 포탑 마디를 물리지 않아도 전투 결과는 한 톨도 달라지지 않는다.
    //
    // ★ **조준 대상을 스스로 찾는다 — `AttackAction`에서 받아오지 않는다.**
    //   액션은 쿨다운이 돌 때까지 대상 탐색을 통째로 건너뛴다(`AttackAction.Tick`의 조기 반환. 무동작
    //   타워가 물리 예산을 태우지 않게 하는 기존 설계다). 거기에 연출을 얹으면 포탑이 **발사 순간에만
    //   홱 돌고 공격 간격 내내 굳어 있다** — 미사일 터렛은 간격이 2.4초라 그 정지가 그대로 눈에 띈다.
    //   그렇다고 액션이 매 프레임 탐색하게 바꾸면, 연출 하나 때문에 모든 공격 타워의 물리 조회가
    //   초당 0.4회에서 60회로 뛴다. 그래서 **탐색 주기를 연출이 자기 몫으로 따로 가진다** — 비용이
    //   이 컴포넌트가 붙은 타워에만, 그것도 `scanInterval`만큼만 생기고 전투 경로는 무변경으로 남는다.
    //
    // ⚠ **사격은 이 회전을 기다리지 않는다.** 액션의 쿨다운과 포탑의 선회는 서로를 모른다 — 얽으면
    //   선회 속도가 곧 DPS 노브가 되어(느린 포탑 = 약한 타워) 연출값이 밸런싱 표 밖에서 화력을 흔든다.
    [RequireComponent(typeof(Tower))]
    public class TowerTurretAim : MonoBehaviour
    {
        [Tooltip("좌우로 도는 마디(미사일 터렛의 'Turret'). 미할당이면 아무것도 하지 않는다 — " +
                 "포탑 마디가 없는 타워에 붙어도 안전하다.")]
        [SerializeField] Transform turret;

        [Tooltip("선회 속도(도/초).")]
        [SerializeField] float turnSpeed = 240f;

        [Tooltip("조준 대상을 다시 고르는 주기(초). 짧을수록 새로 들어온 적에게 빨리 반응하지만 " +
                 "그만큼 물리 조회가 는다. 조준한 적을 따라가는 것은 매 프레임이라 이 값과 무관하다.")]
        [SerializeField] float scanInterval = 0.2f;

        [Tooltip("조준할 적이 없을 때 한 방향으로 계속 도는 대기 회전 속도(도/초). " +
                 "음수면 반대 방향, 0이면 마지막 방향에서 멈춘다. `returnToRest`가 켜져 있으면 무시된다.")]
        [SerializeField] float idleTurnSpeed = 30f;

        [Tooltip("조준할 적이 없으면 설치 당시 방향으로 되돌아간다. 켜면 위의 대기 회전 대신 이 거동을 쓴다 — " +
                 "'적이 사라졌으니 정리하고 제자리로'라는 마무리 연출용이다.")]
        [SerializeField] bool returnToRest;

        [Tooltip("적을 잃은 뒤 TargetLost를 발행하기까지의 유예(초). **0 = 즉시**(기본). " +
                 "유예 안에 적을 다시 잡으면 발행이 취소된다 — 사거리 경계 채터를 여기서 흡수한다.")]
        [SerializeField] float targetLostGrace;

        [Tooltip("탐색 주기마다 조준 상태를 콘솔에 찍는다(진단용). 평소엔 꺼둘 것.")]
        [SerializeField] bool debugLog;

        /// 겨누던 적이 사라져 **대기 상태로 정착한 순간**. 사거리 이탈·사망·밤 종료가 전부 여기로 모인다.
        /// 잡을 적이 없는 동안 매 프레임 나지 않고 한 번만 난다.
        ///
        /// 이 신호를 여기서 내는 이유: 대상 탐색 주기를 이미 이 컴포넌트가 자기 몫으로 들고 있다
        /// (클래스 주석의 ★ 항목). 마무리 연출이 각자 적을 탐색하면 물리 조회가 한 벌 더 늘어나는 것도
        /// 문제지만, 진짜 문제는 **"적이 없다"의 판정 시점이 둘로 갈리는 것**이다 — 포탑은 제자리로
        /// 돌아가는데 장전 모션은 아직 안 나오는 식의 어긋남이 생긴다.
        ///
        /// ★ **"전이"가 아니라 "정착"이다.** 사거리 경계에 적이 걸치면 `scanInterval` 주기로 잃음·재획득이
        ///   반복되어 단순 전이 감지로는 초당 최대 `1/scanInterval`회 발행된다. 그 채터를 **소비처가 아니라
        ///   발행 측에서** `targetLostGrace`로 흡수한다 — 소비처마다 유예를 복붙하면 같은 의미가 여러 구현으로
        ///   갈라지고, 이 이벤트는 앞으로 소비처가 늘어날 자리다(사운드·파티클·UI).
        public event System.Action TargetLost;

        /// `TargetLost`가 실제로 발행될 수 있는 상태인지. **`turret`이 미할당이면 `LateUpdate`가 맨 위에서
        /// 조기 반환해 `Rest()`에 영영 도달하지 못하므로 이 이벤트는 한 번도 나지 않는다.**
        ///
        /// 공개하는 이유: 이 컴포넌트는 계약상 **연출 전용**이고 붙이지 않아도 전투 결과는 달라지지 않지만,
        /// `TowerAnimationVisual`이 **루프로 저작된 발사 상태를 빠져나오는 유일한 신호**로 이 이벤트를 쓴다
        /// (FattyPoly Part4의 `MachineGun`·`Minigun`은 `Fire` 클립이 `m_LoopTime: 1`이고 `Fire`에서 나가는
        /// 전이가 전부 조건부라 무조건 탈출이 없다 — 팩마다 `Fire` 저작이 정반대다). 그래서 **연출 필드 하나가
        /// 비면 밤새 발사 모션이 반복되는데 경고도 예외도 없다.** 그 침묵을 소비처가 저작 시점에 깨도록
        /// 상태만 읽기로 내보낸다 — 정지 책임을 이 컴포넌트가 지는 것은 아니다(WL-193).
        public bool PublishesTargetLost => turret != null;

        Tower _tower;
        Transform _target;
        float _scanTimer;
        bool _hadTarget;
        // 발행 대기 중인 "잃음"과 그 시작 시각. 유예 안에 적을 다시 잡으면 대기가 취소된다.
        bool _lostPending;
        float _lostAt;
        Quaternion _restLocalRotation;

        void Awake()
        {
            _tower = GetComponent<Tower>();

            // 설치 당시 방향 = **프리팹에 저작된 각도**. OnEnable이 아니라 Awake에서 한 번만 잡는다 —
            // 재활성화(풀 재사용) 시점에는 포탑이 이미 적을 향해 돌아가 있을 수 있어서, 그때 잡으면
            // "제자리"가 매번 달라진다.
            if (turret != null) _restLocalRotation = turret.localRotation;
        }

        void OnEnable()
        {
            _target = null;
            _hadTarget = false;   // 재활성화 직후 "잃었다" 통지가 헛돌지 않게
            _lostPending = false;
            _scanTimer = 0f;      // 활성화 즉시 1회 탐색 — 배치하자마자 적을 바라보게 한다
        }

        // Update가 아니라 LateUpdate인 이유: 적의 위치는 몬스터 쪽 Update가 옮긴다. 같은 프레임의
        // 최신 위치를 보려면 그 뒤에 서야 한다 — Update에 두면 한 프레임 늦은 위치를 따라가
        // 빠르게 지나가는 적에게서 미세하게 뒤처져 보인다.
        void LateUpdate()
        {
            if (turret == null) return;

            // 낮에는 적이 없다. 호스트가 이미 계산해 둔 페이즈 값을 읽어 탐색 자체를 접는다 —
            // 여기서 DayNightManager를 따로 폴링하면 페이즈 규칙이 갈라진다(WL-044).
            // 낮에는 적이 아예 없다 — 탐색은 접고(호스트가 계산해 둔 페이즈 값을 읽는다. 여기서
            // DayNightManager를 따로 폴링하면 페이즈 규칙이 갈라진다, WL-044) 대기 회전만 돌린다.
            // 배치가 낮에 이뤄지므로 **설치 직후 포탑이 도는 모습은 여기서 나온다.**
            if (!_tower.IsCombatPhase)
            {
                // 밤에 마지막으로 잡았던 대상을 남겨두지 않는다 — 다음 밤 첫 탐색 전까지 이미
                // 사라진 적을 향해 굳어 있게 된다.
                _target = null;
                Rest();
                if (debugLog) LogThrottled("낮 — 탐색 없이 대기");
                return;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = Mathf.Max(scanInterval, 0.02f);   // 0 이하 폭주 방지 하한

                // 대상 선정은 호스트가 소유한다 — 공격 액션과 **같은 정의**를 쓰므로 포탑이 겨눈 적과
                // 실제로 맞는 적이 갈라지지 않는다. 같은 프레임에 액션이 이미 조회했다면 캐시가 오므로
                // 발사 프레임에도 물리 조회는 1회다.
                IDamageable target = _tower.AcquireTarget();
                _target = target?.HitPosition;

                if (debugLog)
                    Debug.Log($"[TurretAim] {name}: 사거리={_tower.AttackRange:0.#} 대상=" +
                              (_target == null ? "없음" : _target.root.name) +
                              $" 포탑각={turret.eulerAngles.y:0}도", this);
            }

            // 대상이 없거나(사거리 밖) 죽어 파괴됐으면 대기 거동으로 돌아간다.
            if (_target == null)
            {
                Rest();
                return;
            }

            _hadTarget = true;
            _lostPending = false;   // 유예 안에 다시 잡았다 → 마무리 통지 취소(경계 채터 흡수)

            // 수평 성분만 쓴다 — 앙각은 모델이 저작한 값이 정본이다. 대상을 향해 포신을 내리면
            // 곡사 실루엣(캐논의 `Top` -45°)이 깨지고, 선회는 연출 전용이므로 판정과 어긋나도 무해하다.
            //
            // ⚠ **더 이상 "발사도 수평이라서"가 근거가 아니다.** `AttackAction.TryAttack`은 이제
            // `aimDir`의 Y를 살려 실제로 아래를 겨눈다(타워는 Grass 윗면 3.80+, 적은 Road 윗면 0.80을
            // 걷는다 — 그 주석 참조). 즉 **연출은 수평, 판정은 3D**로 갈라져 있다. 이 컴포넌트가
            // 앙각까지 따라가게 만들려면 마디에 저작된 앙각을 어떻게 보존할지부터 정해야 하므로
            // (`turret.rotation` 대입이 앙각을 지우는 문제, 2026-08-12 캐논 사고) 여기서는 손대지 않는다.
            Vector3 direction = _target.position - turret.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;   // 바로 위/아래 — 수평 방향이 정의되지 않는다

            // ★ **회전을 대입하지 않고 월드 Y축 델타만 얹는다.** `turret.rotation = LookRotation(...)`으로
            //   덮어쓰면 LookRotation의 결과가 앙각 0이라 **마디에 저작된 앙각이 지워진다.**
            //   미사일 터렛은 앙각이 자식(`MissileG_Barrel` -30°)에 있어 문제가 드러나지 않았지만,
            //   캐논(`CandyCanon`)은 `turret`이 앙각을 **자기 자신**에 가진 마디(`Top` -45°)라
            //   조준이 시작되는 순간 포신이 평평해져 박격포가 직사포가 됐다(2026-08-12).
            //   대기 회전(`IdleTurn`)이 이미 같은 방식이라 축도 일치한다 — 조준↔대기 전환에서 튀지 않는다.
            //
            //   ⚠ 앙각이 자식에 있는 프리팹(미사일)에서는 `turret.forward`가 이미 수평이라
            //     **거동이 종전과 완전히 동일하다.** 캐논만 고쳐지고 회귀는 없다.
            Vector3 flatForward = turret.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f) return;   // 포신이 수직 — 수평 방위가 정의되지 않는다

            float delta = Vector3.SignedAngle(flatForward, direction, Vector3.up);
            float step = Mathf.Clamp(delta, -turnSpeed * Time.deltaTime, turnSpeed * Time.deltaTime);
            turret.Rotate(Vector3.up, step, Space.World);
        }

        /// 조준할 적이 없을 때의 거동. 유예가 지나면 **한 번만** 통지하고, 그 뒤로는 설정에 따라
        /// 제자리로 돌아가거나(`returnToRest`) 대기 회전을 돈다.
        void Rest()
        {
            if (_hadTarget)
            {
                _hadTarget = false;
                _lostPending = true;
                _lostAt = Time.time;
            }

            // 유예 0이면 같은 프레임에 통과해 기존 거동과 동일하다(기본값 무변경).
            // `Time.time`(스케일드)을 쓰는 것은 `scanInterval`이 `Time.deltaTime`으로 도는 것과 같은 축이라,
            // 배속에서 탐색 주기와 유예가 함께 줄어 채터 흡수 비율이 유지되기 때문이다.
            if (_lostPending && Time.time - _lostAt >= targetLostGrace)
            {
                _lostPending = false;
                TargetLost?.Invoke();
            }

            if (returnToRest) ReturnToRest();
            else IdleTurn();
        }

        /// 설치 당시 각도로 되돌아간다. 월드가 아니라 **로컬** 회전을 보간하는 이유는 "제자리"가
        /// 타워 몸체 기준이기 때문이다 — 배치할 때 타워가 어느 방향으로 놓였든 포탑은 몸체에 대해
        /// 저작된 각도로 돌아간다. 도착하면 `RotateTowards`가 그 값에서 더 움직이지 않으므로 멈춘다.
        void ReturnToRest()
            => turret.localRotation = Quaternion.RotateTowards(
                turret.localRotation, _restLocalRotation, turnSpeed * Time.deltaTime);

        /// 조준할 적이 없을 때의 대기 회전. **한 방향으로 계속 돈다** — 좌우로 훑는 스윕이 아니다.
        ///
        /// 왕복 스윕은 양 끝에서 방향이 꺾이는 순간이 눈에 걸려(멈춤 → 반대로 출발) 여러 대가 나란히
        /// 서 있으면 그 꺾임이 동시에 보인다. 한 방향 회전은 꺾이는 지점이 없어 배경으로 가라앉는다.
        ///
        /// 조준으로 넘어갈 때 초기화가 필요 없다 — 지금 각도에서 `RotateTowards`가 이어받으므로
        /// 적을 잡는 순간 튀지 않고 그대로 조준으로 미끄러진다. 반대로 적을 놓치면 그 각도에서
        /// 다시 돌기 시작한다.
        void IdleTurn()
        {
            if (Mathf.Approximately(idleTurnSpeed, 0f)) return;   // 0 = 마지막 방향에서 멈춤

            // 월드 up 기준이라 앙각(포신의 -30°)과 부모 회전에 관계없이 수평 회전만 얹힌다 —
            // 조준 경로가 수평 성분만 쓰는 것과 같은 축을 지킨다.
            turret.Rotate(Vector3.up, idleTurnSpeed * Time.deltaTime, Space.World);
        }

        // 낮 동안 매 프레임 찍히면 콘솔이 잠기므로 같은 문구는 1초에 한 번만 낸다(진단용 한정).
        float _logTimer;

        void LogThrottled(string message)
        {
            _logTimer -= Time.deltaTime;
            if (_logTimer > 0f) return;
            _logTimer = 1f;
            Debug.Log($"[TurretAim] {name}: {message}", this);
        }

    }
}
