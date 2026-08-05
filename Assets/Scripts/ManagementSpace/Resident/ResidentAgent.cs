using UnityEngine;
using UnityEngine.AI;

// 주민 BT 커스텀 리프 노드가 참조하는 유일한 컴포넌트(#276).
//
// 보스 BT의 EnemyAgent와 같은 역할·같은 규약이다(Docs/Monster/Boss/BossNodeReference.md 「작성 규약」).
// 노드는 이 파사드만 보고 NavMeshAgent / Animator / Resident에 직접 닿지 않는다
// (Docs/ManagementArea/Resident.md §1 컴포넌트 구성).
//
// 무상태 파사드다. 위치는 NavMeshAgent가, 재생 상태는 Animator가 소유하고 여기서는 전달만 한다 —
// 양쪽이 같은 값을 들면 동기화가 깨진다.
//
// 네임스페이스를 두지 않는다 — 커스텀 노드와 같은 규약. 클래스 이름이 전역에서 유일해야 한다.
[RequireComponent(typeof(NavMeshAgent))]
public class ResidentAgent : MonoBehaviour
{
    // 산책 목적지를 NavMesh 위로 끌어올 때 허용하는 최대 거리. 표본점이 NavMesh 밖으로 떨어져도
    private NavMeshAgent navAgent;
    private Animator animator;

    // Animator에 매 프레임 같은 값을 밀어 넣지 않기 위한 캐시.
    // SetBool 자체는 싸지만, 상태 전이 판정이 매 프레임 재평가되는 것을 피한다.
    private bool isMovingCached;

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    private void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();

        // 자식까지 탐색: Animator는 모델 자식에 붙는 프리팹 구성이 흔하다(EnemyAgent와 같은 이유, WL-093).
        animator = GetComponentInChildren<Animator>();

        // 조용한 무동작을 막는다 — 참조가 빠지면 노드는 성공을 반환하는데 아무 일도 일어나지 않는다.
        if (animator == null)
        {
            Debug.LogWarning($"[{name}] Animator를 찾지 못해 걷기/유휴 전환이 동작하지 않습니다.", this);
        }
    }

    // ── 상태 조회 ─────────────────────────────

    public Vector3 Position => transform.position;

    // NavMesh 위에 올라가 있는가. 스폰 위치가 NavMesh 밖이면 목적지 지정이 조용히 무시되므로
    // 이동 노드가 시작 시점에 이걸 먼저 본다.
    public bool IsOnNavMesh => navAgent != null && navAgent.isOnNavMesh;

    // ── 목적지 ─────────────────────────────
    //
    // 목적지를 **뽑는** 책임은 여기 없다. ResidentWaypoint가 자기 반경 안의 한 점을 돌려주고,
    // BT 노드가 그것을 받아 아래 TrySetDestination으로 넘긴다 — 주민은 "어디로 갈지"를 모르고
    // "가라는 곳으로 가는" 역할만 한다.

    public bool TrySetDestination(Vector3 destination)
    {
        // isOnNavMesh를 먼저 본다. NavMesh 밖에서 SetDestination을 부르면 Unity가 에러를 뱉고
        // false를 반환하는데, 그 에러가 개체마다 매 프레임 쏟아지면 콘솔이 묻힌다.
        if (navAgent == null || !navAgent.isOnNavMesh)
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

    // ── 애니메이션 ─────────────────────────────

    // 걷기/유휴 전환. Animator의 Bool 하나로 표현한다 —
    // 클립을 골라야 하는 행위(R4 수다 등)가 들어오면 그때 노드가 클립을 직접 지정하는 축을 따로 연다.
    public void SetMoving(bool value)
    {
        if (animator == null || isMovingCached == value)
        {
            return;
        }

        isMovingCached = value;
        animator.SetBool(IsMovingHash, value);
    }
}
