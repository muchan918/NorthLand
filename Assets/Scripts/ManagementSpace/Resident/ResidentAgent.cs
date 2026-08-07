using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 주민 BT 커스텀 리프 노드가 참조하는 유일한 컴포넌트(#276).
//
// 보스 BT의 EnemyAgent와 같은 역할·같은 규약이다(Docs/Monster/Boss/BossNodeReference.md 「작성 규약」).
// 노드는 이 파사드만 보고 NavMeshAgent / Animator / Resident에 직접 닿지 않는다
// (Docs/ManagementArea/Resident.md §1 컴포넌트 구성).
//
// 무상태 파사드다. 위치는 NavMeshAgent가, 재생 상태는 Animator가, **대화 상태는 Resident가** 소유하고
// 여기서는 전달만 한다 — 양쪽이 같은 값을 들면 동기화가 깨진다.
//
// 네임스페이스를 두지 않는다 — 커스텀 노드와 같은 규약. 클래스 이름이 전역에서 유일해야 한다.
[RequireComponent(typeof(NavMeshAgent))]
public class ResidentAgent : MonoBehaviour
{
    // 유휴/걷기로 돌아갈 때 물릴 상태 이름. 컨트롤러의 상태 이름과 일치해야 한다.
    // 이 둘만 상수로 박는다 — Bool IsMoving으로 오가는 짝이라 노드가 골라야 할 것이 없고,
    // 임의 클립 재생(PlayState)은 반대로 상태 이름을 전부 노드가 지정한다.
    private const string IdleState = "Idle";

    // 오프메시 복구를 시도할 최대 거리(<see cref="EnsureOnNavMesh"/>). 주민 반경(0.6)의 몇 배 정도면
    // 밀려나 벗어난 경우를 덮는다. 더 키우면 벽 너머로 끌어올려 순간이동처럼 보인다.
    private const float OffMeshRecoverDistance = 2f;

    private NavMeshAgent navAgent;

    // 프리팹이 정한 회피 방식. 대화 중에는 회피를 끄고, 끝나면 이 값으로 되돌린다.
    private ObstacleAvoidanceType baseAvoidance;
    private Animator animator;
    private Resident resident;

    // Animator에 매 프레임 같은 값을 밀어 넣지 않기 위한 캐시.
    // SetBool 자체는 싸지만, 상태 전이 판정이 매 프레임 재평가되는 것을 피한다.
    private bool isMovingCached;

    // 캐시를 한 번 무효화한다. PlayState로 Idle/Walk 밖으로 나갔다 돌아올 때 쓴다 —
    // 캐시된 값과 요청 값이 같아서 SetBool이 생략되면, 임의 클립 상태에 갇혀 영구히 못 돌아온다.
    private bool locomotionDirty;

    // GetCurrentAnimatorClipInfo(0)은 호출마다 배열을 새로 만든다. 턴마다 한 번 부르는 값이라
    // 큰 비용은 아니지만, 30명이 도는 앰비언트 시스템에서 굳이 할당을 만들 이유가 없다.
    private readonly List<AnimatorClipInfo> clipInfoBuffer = new List<AnimatorClipInfo>();

    // 프리팹이 정한 기준 이동속도. 배수의 분모라 Awake에 한 번만 캡처한다 —
    // 매번 현재 speed에 곱하면 배수가 누적돼 주민이 점점 빨라진다.
    private float baseSpeed = 1f;

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    private void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        baseSpeed = navAgent != null && navAgent.speed > 0f ? navAgent.speed : 1f;

        // 프리팹이 정한 회피 방식. 대화 중 잠시 껐다가 이 값으로 되돌린다(SetStationaryHold).
        baseAvoidance = navAgent != null ? navAgent.obstacleAvoidanceType : ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        // 자식까지 탐색: Animator는 모델 자식에 붙는 프리팹 구성이 흔하다(EnemyAgent와 같은 이유, WL-093).
        animator = GetComponentInChildren<Animator>();

        // 같은 오브젝트에 병존한다. Awake 순서와 무관하게 GetComponent는 성립한다 —
        // 서로의 Awake 결과를 참조하지 않기 때문이다.
        resident = GetComponent<Resident>();

        // 조용한 무동작을 막는다 — 참조가 빠지면 노드는 성공을 반환하는데 아무 일도 일어나지 않는다.
        if (animator == null)
        {
            Debug.LogWarning($"[{name}] Animator를 찾지 못해 걷기/유휴 전환이 동작하지 않습니다.", this);
        }

        if (resident == null)
        {
            Debug.LogWarning($"[{name}] Resident를 찾지 못해 인사·대화 노드가 동작하지 않습니다.", this);
        }

        SpreadAvoidancePriority();
    }

    // 회피 우선순위를 개체마다 흩는다.
    //
    // 프리팹 값이 그대로면 주민 전원이 같은 우선순위(50)를 갖는다. `NavMeshAgent`의 회피는 **우선순위가
    // 같은 상대를 서로 양보하지 않아** 좁은 곳에서 두 명이 붙은 채 밀고 서는 그림이 나온다.
    // 값을 흩으면 매 쌍에서 한쪽이 양보해 저절로 풀린다.
    //
    // 프리팹을 셋으로 나누지 않고 런타임에 흩는 이유: 프리팹당 하나면 같은 프리팹끼리(각 10명) 다시 같아진다.
    private void SpreadAvoidancePriority()
    {
        if (navAgent == null)
        {
            return;
        }

        // 30~70. 0(최상위)이나 99(최하위)까지 벌리지 않는다 — 나중에 다른 유닛과 섞일 때
        // 주민이 그 축의 양 끝을 점유하고 있으면 상대적 우선순위를 줄 자리가 없다.
        navAgent.avoidancePriority = Random.Range(30, 71);
    }

    // ── 상태 조회 ─────────────────────────────

    public Vector3 Position => transform.position;

    // 신원·상태의 정본. 노드가 세션을 만들거나 상대를 세울 때 이걸 경유한다.
    public Resident Resident => resident;

    public bool HasConversation => resident != null && resident.Conversation != null;

    // NavMesh 위에 올라가 있는가. **관측 전용이고 부작용이 없다** — 디버그·검증에서 상태만 볼 때 쓴다.
    //
    // ⚠ **이동 경로에서는 이걸 쓰지 말 것.** 이동 노드는 아래 `EnsureOnNavMesh`를 부른다. 이름이 비슷하지만
    //   그쪽은 벗어난 주민을 `Warp`로 끌어올리는 **부작용이 있는 복구 함수**다. 잘못 고르면 증상이 조용히
    //   갈린다 — 이쪽을 쓰면 굳은 주민이 영영 안 풀리고, 저쪽을 관측용으로 쓰면 의도치 않은 순간이동이 섞인다.
    public bool IsOnNavMesh => navAgent != null && navAgent.isOnNavMesh;

    // NavMesh 밖으로 밀려났으면 가장 가까운 지점으로 끌어올린다. 성공했거나 원래 위에 있었으면 참.
    //
    // **왜 필요한가**: 오프메시가 되면 `SetDestination`이 통째로 무시되므로 주민이 그 자리에서 영구히 굳는다.
    // 스폰 위치 오류는 <see cref="ResidentSpawner"/>가 걸러 주지만, 지역 회피에 밀리거나 발밑 지형이
    // 바뀌면 **런타임에** 벗어날 수 있다 — 그건 아무도 되돌려 주지 않는다.
    //
    // `Warp`를 쓴다. `transform.position` 대입은 Agent 내부 위치와 어긋나 다음 프레임에 되돌아간다.
    public bool EnsureOnNavMesh()
    {
        if (navAgent == null || !navAgent.isActiveAndEnabled)
        {
            return false;
        }

        if (navAgent.isOnNavMesh)
        {
            return true;
        }

        // 반경은 넉넉히 잡지 않는다 — 멀리서 끌어오면 순간이동으로 보인다. 이 거리로도 못 찾으면
        // 베이크 자체가 없는 곳이라 끌어와도 곧 다시 벗어난다.
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, OffMeshRecoverDistance, NavMesh.AllAreas))
        {
            return false;
        }

        return navAgent.Warp(hit.position);
    }

    // ── 목적지 ─────────────────────────────
    //
    // 목적지를 **뽑는** 책임은 여기 없다. ResidentWaypoint가 자기 반경 안의 한 점을 돌려주고,
    // BT 노드가 그것을 받아 아래 TrySetDestination으로 넘긴다 — 주민은 "어디로 갈지"를 모르고
    // "가라는 곳으로 가는" 역할만 한다.

    public bool TrySetDestination(Vector3 destination)
    {
        // isOnNavMesh를 먼저 본다. NavMesh 밖에서 SetDestination을 부르면 Unity가 에러를 뱉고
        // false를 반환하는데, 그 에러가 개체마다 매 프레임 쏟아지면 콘솔이 묻힌다.
        // 벗어나 있으면 한 번 끌어올려 본다 — 여기가 모든 이동 지시가 지나는 길목이라, 복구를 여기 두면
        // 호출부(이동·대화·귀가 노드)가 각자 챙기지 않아도 된다.
        if (!EnsureOnNavMesh())
        {
            return false;
        }

        navAgent.isStopped = false;
        return navAgent.SetDestination(destination);
    }

    // 경로 계산이 끝나고 목적지에 닿았는가.
    //
    // pathPending을 먼저 보지 않으면 SetDestination 직후 remainingDistance가 0으로 읽혀
    // "출발하자마자 도착"으로 오판된다. EnemyPlayAnimationAction이 전이 구간을 배제하는 것과 같은 계열의 함정이다.
    public bool HasArrived
    {
        get
        {
            if (navAgent == null || !navAgent.isOnNavMesh)
            {
                return true;
            }

            if (navAgent.pathPending)
            {
                return false;
            }

            if (navAgent.remainingDistance > navAgent.stoppingDistance)
            {
                return false;
            }

            // 경로가 남아 있지 않거나 실제로 멈춰 섰을 때만 도착으로 본다.
            return !navAgent.hasPath || navAgent.velocity.sqrMagnitude < 0.01f;
        }
    }

    // 목적지까지 남은 거리. 경로 계산 중에는 아직 값이 없으므로 무한대로 본다 —
    // "도착 임박이면 쉬지 않는다"(R15) 판정이 계산 전 0을 읽고 오판하는 것을 막는다.
    public float RemainingDistance
    {
        get
        {
            if (navAgent == null || !navAgent.isOnNavMesh || navAgent.pathPending)
            {
                return float.PositiveInfinity;
            }

            return navAgent.remainingDistance;
        }
    }

    // 이동 속도 배수(R8 귀가는 뛴다).
    //
    // 기준 속도는 프리팹의 NavMeshAgent 값이다 — 여기서 **배수만** 걸고 원본은 Awake에 캡처해 둔다.
    // 노드가 절대값을 쓰면 프리팹에서 속도를 조정했을 때 그 노드만 따라오지 않는다.
    // 켠 노드가 OnEnd에서 1로 되돌린다(BossNodeReference 「작성 규약」).
    public float SpeedFactor
    {
        get => navAgent != null && baseSpeed > 0f ? navAgent.speed / baseSpeed : 1f;
        set
        {
            if (navAgent == null)
            {
                return;
            }

            navAgent.speed = baseSpeed * Mathf.Max(0.01f, value);
        }
    }

    // 이동을 끝낸다. 경로까지 지우므로 이후 목적지는 새로 지정해야 한다.
    public void StopMoving()
    {
        if (navAgent == null || !navAgent.isOnNavMesh)
        {
            return;
        }

        navAgent.isStopped = true;
        navAgent.ResetPath();
    }

    // 걷던 것을 잠깐 멈춘다(R15 휴식). **경로를 지우지 않는다** —
    // StopMoving은 ResetPath까지 하므로 여기에 쓸 수 없다. 재개하면 가던 길을 그대로 이어 간다.
    /// 제자리에 **못을 박는다**. 대화 중 서 있는 동안 켠다.
    ///
    /// `isStopped`만으로는 부족하다 — 정지한 `NavMeshAgent`도 지역 회피 해에 따라 밀려난다.
    /// 그래서 원래 버그(걸어가던 주민이 대화 중인 둘을 밀어냄)가 났고, 회피물을 세웠을 때는
    /// **회피물이 참가자를 밀어내 무한히 튕겨 나가는 되먹임**이 났다(중심거리 5 → 47, 속도 65 실측).
    ///
    /// 자기 회피를 끄면 남이 밀어도, 회피물이 밀어도 움직이지 않는다. 반대로 **남들은 여전히 이 주민을
    /// 피해 간다** — 회피는 각자 자기 몫을 푸는 것이고, 끄는 것은 "내가 남을 피하지 않는다"일 뿐이다.
    public void SetStationaryHold(bool hold)
    {
        if (navAgent == null)
        {
            return;
        }

        navAgent.obstacleAvoidanceType = hold ? ObstacleAvoidanceType.NoObstacleAvoidance : baseAvoidance;
    }

    public void PauseMovement()
    {
        if (navAgent == null || !navAgent.isOnNavMesh)
        {
            return;
        }

        navAgent.isStopped = true;
    }

    public void ResumeMovement()
    {
        if (navAgent == null || !navAgent.isOnNavMesh)
        {
            return;
        }

        navAgent.isStopped = false;
    }

    // ── 회전 ─────────────────────────────
    //
    // 평소 회전은 NavMeshAgent가 소유한다(updateRotation = true, 진행 방향을 본다). 그런데 **멈추면
    // 속도가 0이라 회전 자체가 일어나지 않는다** — 그래서 "마주 보고 정지"(§7.1)를 Agent에게 맡길 수 없다.
    //
    // FaceTowards가 소유권을 빼앗고, ReleaseRotation이 되돌린다. 되돌리는 것을 잊으면 주민이 이후로
    // 영원히 옆걸음으로 걷는다 — 노드가 OnEnd에서 반드시 반납한다(BossNodeReference 「작성 규약」).

    // 매 프레임 불러서 조금씩 돌린다. 즉시 스냅하지 않는 이유: 두 명이 한 프레임에 정확히 마주 보면
    // 인사가 아니라 순간이동으로 읽힌다. 회전 속도는 NavMeshAgent의 값을 그대로 쓴다 —
    // 걸을 때와 멈춰 있을 때의 회전 감각이 갈리지 않게.
    public void FaceTowards(Vector3 worldPoint)
    {
        Vector3 direction = worldPoint - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        if (navAgent != null)
        {
            navAgent.updateRotation = false;
        }

        float degreesPerSecond = navAgent != null ? navAgent.angularSpeed : 360f;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(direction),
            degreesPerSecond * Time.deltaTime);
    }

    public bool IsFacing(Vector3 worldPoint, float toleranceDegrees)
    {
        Vector3 direction = worldPoint - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return true;
        }

        return Vector3.Angle(transform.forward, direction) <= toleranceDegrees;
    }

    public void ReleaseRotation()
    {
        if (navAgent != null)
        {
            navAgent.updateRotation = true;
        }
    }

    // ── 애니메이션 ─────────────────────────────
    //
    // 두 축이 있다:
    //  · 걷기/유휴 — Bool IsMoving으로 컨트롤러가 알아서 오간다(SetMoving)
    //  · 임의 클립 — 노드가 상태 이름을 지정해 직접 물린다(PlayState → ReturnToLocomotion)
    //
    // 왜 트리거가 아니라 CrossFade인가: 트리거로 하면 클립마다 파라미터 + AnyState 전이 + 복귀 전이를
    // 저작해야 하고, 클립이 늘 때마다 같은 배선을 반복한다. CrossFade는 전이가 없는 고립 상태만 있으면
    // 되므로 컨트롤러가 클립 수만큼만 커진다. 대신 **나가는 길이 없으므로 복귀를 노드가 지시해야 한다.**

    // 걷기/유휴 전환.
    public void SetMoving(bool value)
    {
        if (animator == null || (!locomotionDirty && isMovingCached == value))
        {
            return;
        }

        locomotionDirty = false;
        isMovingCached = value;
        animator.SetBool(IsMovingHash, value);
    }

    // 이름으로 상태를 물린다(R3 인사 · R4 수다 · R7 놀람 · R12 웃음).
    //
    // ⚠ CrossFade가 아니라 CrossFadeInFixedTime을 쓴다. CrossFade의 duration은 **현재 상태의
    //   정규화 시간** 단위라, 0.15를 넘기면 클립 길이에 따라 전이가 0.19초(Idle 1.33)에서
    //   1.54초(Talking_1 10.27)까지 벌어진다. 초 단위로 고정해야 §7.2의 "짧은 크로스페이드"가 성립한다.
    public bool PlayState(string stateName, float fadeSeconds)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
        {
            return false;
        }

        // 컨트롤러가 Idle/Walk 밖으로 나갔다. 복귀 시 SetBool이 캐시 때문에 생략되면 돌아올 길이 없다.
        locomotionDirty = true;

        animator.CrossFadeInFixedTime(stateName, Mathf.Max(0f, fadeSeconds));
        return true;
    }

    // 유휴/걷기 축으로 돌아온다. PlayState로 물린 상태에는 나가는 전이가 없으므로 이 호출이 유일한 복귀 경로다.
    //
    // 정지 상태로 되돌린다 — 대화가 끝난 뒤의 재개는 이동 노드가 목적지를 다시 지정할 때 일어난다.
    public void ReturnToLocomotion(float fadeSeconds)
    {
        if (animator == null)
        {
            return;
        }

        SetMoving(false);
        animator.CrossFadeInFixedTime(IdleState, Mathf.Max(0f, fadeSeconds));
    }

    // 현재 상태의 재생 진행도(1 = 1회 재생 완료). 루프 클립이면 1을 넘어 계속 증가한다 —
    // 수다 클립(루프)의 "한 바퀴 돌았다"도 이 값으로 잡힌다.
    // Animator가 없으면 1을 반환한다 — 대기 노드가 무한 Running으로 브랜치를 붙잡지 않게.
    public float AnimationNormalizedTime =>
        animator != null ? animator.GetCurrentAnimatorStateInfo(0).normalizedTime : 1f;

    // 전이 중이면 참. CrossFade 직후 몇 프레임은 아직 이전 상태가 읽히므로, 종료 판정에서 이 구간을
    // 배제해야 이전 상태의 진행도(이미 1 초과)를 보고 "시작도 전에 끝났다"로 오판하지 않는다.
    // EnemyPlayAnimationAction이 밟은 함정과 같은 것이다.
    public bool IsAnimatorInTransition => animator != null && animator.IsInTransition(0);

    // 요청한 상태에 실제로 도착했는가. 종료 판정의 전제다.
    //
    // 보스 노드는 "전이를 한 번 봤다"는 래치로 이 구간을 넘겼는데, 이름을 직접 확인하는 것이 더 확실하다 —
    // 페이드가 0이거나 프레임이 길어 전이를 한 프레임도 관측하지 못하면 래치가 서지 않아 영구 대기가 된다.
    // 이름 확인은 그 경우에도 성립하고, **상태 이름이 컨트롤러에 없을 때 영원히 거짓**이라
    // 오타를 상한 타이머로 잡아낼 수 있다.
    public bool IsInState(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
        {
            return false;
        }

        return animator.GetCurrentAnimatorStateInfo(0).IsName(stateName);
    }

    // 지금 물려 있는 클립의 길이(초). 화자가 자기 턴의 길이를 세션에 등록하는 데 쓴다(§7.2 R12).
    //
    // 클립 길이를 코드나 그래프에 적어 두지 않는 이유: Laughing·Surprised처럼 트림 구간을 조정하면
    // 길이가 바뀌는데(§5.2), 적어 둔 값은 조용히 어긋난다. Animator에서 읽으면 저절로 따라온다.
    // 전이 중에는 **이전 상태의** 클립이 읽히므로 전이가 끝난 뒤에 물어야 한다.
    public float CurrentStateLength
    {
        get
        {
            if (animator == null)
            {
                return 0f;
            }

            animator.GetCurrentAnimatorClipInfo(0, clipInfoBuffer);

            return clipInfoBuffer.Count > 0 && clipInfoBuffer[0].clip != null
                ? clipInfoBuffer[0].clip.length
                : 0f;
        }
    }

    // 지금 재생 중인 클립 이름. 디버그 표시용이다(ResidentDebugView).
    //
    // 상태 이름이 아니라 클립 이름을 쓴다 — Animator는 런타임에 상태 **해시**만 주므로 이름을 되돌릴 수
    // 없지만, 클립은 에셋이라 이름이 그대로 남아 있다. 상태와 클립이 1:1이라(§11.4) 구분에 차이가 없다.
    public string CurrentClipName
    {
        get
        {
            if (animator == null)
            {
                return null;
            }

            animator.GetCurrentAnimatorClipInfo(0, clipInfoBuffer);

            return clipInfoBuffer.Count > 0 && clipInfoBuffer[0].clip != null
                ? clipInfoBuffer[0].clip.name
                : null;
        }
    }
}
