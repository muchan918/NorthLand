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
                 "음수면 반대 방향, 0이면 마지막 방향에서 멈춘다.")]
        [SerializeField] float idleTurnSpeed = 30f;

        [Tooltip("탐색 주기마다 조준 상태를 콘솔에 찍는다(진단용). 평소엔 꺼둘 것.")]
        [SerializeField] bool debugLog;

        Tower _tower;
        Transform _target;
        float _scanTimer;

        void Awake() => _tower = GetComponent<Tower>();

        void OnEnable()
        {
            _target = null;
            _scanTimer = 0f;   // 활성화 즉시 1회 탐색 — 배치하자마자 적을 바라보게 한다
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
                IdleTurn();
                if (debugLog) LogThrottled("낮 — 탐색 없이 대기 회전");
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

            // 대상이 없거나(사거리 밖) 죽어 파괴됐으면 대기 회전으로 돌아간다.
            if (_target == null)
            {
                IdleTurn();
                return;
            }

            // 수평 성분만 쓴다 — 앙각은 모델이 저작한 값(`MissileG_Barrel`의 -30°)이 정본이다.
            // 대상을 향해 포신을 내리면 하늘로 쏘아 올리는 미사일 런처의 실루엣이 깨지고, 발사 자체도
            // 수평 조준으로 계산되므로(`AttackAction.TryAttack`이 aimDir.y를 0으로 둔다) 맞출 상대도 없다.
            Vector3 direction = _target.position - turret.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;   // 바로 위/아래 — 수평 방향이 정의되지 않는다

            // 포신은 모델에서 +Z를 향해 저작돼 있다(`ShootPoint`가 Turret 로컬 +Z에 있다) — 그래서
            // 보정 오프셋 없이 LookRotation을 그대로 쓴다. 모델을 교체할 때 이 전제가 깨지면 포탑이
            // 옆을 보며 쏘게 되므로, 교체 시 ShootPoint의 로컬 좌표를 먼저 확인할 것.
            Quaternion desired = Quaternion.LookRotation(direction.normalized, Vector3.up);
            turret.rotation = Quaternion.RotateTowards(turret.rotation, desired, turnSpeed * Time.deltaTime);
        }

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
