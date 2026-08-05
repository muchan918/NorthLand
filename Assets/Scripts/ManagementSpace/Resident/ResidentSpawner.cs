using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// 군중의 인원과 밤낮 출입을 관리한다(#276, §3). 씬에 하나 둔다.
///
/// ── 경계 규칙 ──────────────────────────────────────────────────
///
/// **주민 캐릭터는 자원 로직을 소유하지 않는다**(§1). 배치 수의 단일 진실 원천은 끝까지
/// <see cref="ManagementController"/>이고, 이 스포너는 그 숫자를 **구독만** 해서
/// 군중의 크기로 번역한다 — `화면의 주민 수 = N − AssignedTotal`(§3.1).
///
/// ── 아침에 한 문에서 여럿이 나오는 문제 ─────────────────────────
///
/// 같은 지점에서 나온 주민은 서로의 코앞에 서게 되어 **나오자마자 대화가 성립한다.** 다섯 가지로 나눠 막는다.
/// 앞의 넷은 "물리적으로 뭉치는 것"을, 마지막 하나는 "서로를 첫 대화 상대로 고르는 것"을 다룬다.
///
/// | | 대책 | 어디서 |
/// |---|---|---|
/// | ① | **문에 고르게 배분** — 문 20개에 주민 30명이면 문당 1~2명이 되어 문제의 크기 자체가 줄어든다 | 이 클래스 |
/// | ② | **순차 등장** — 한 명씩 간격을 두고 내보낸다 | 이 클래스 |
/// | ③ | **퇴장 유예** — 문의 +Z로 D유닛 직진하는 동안 BT를 평가하지 않는다 | `ResidentExitDoorAction` |
/// | ④ | **목적지 선지정** — 유예가 끝날 때 웨이포인트를 들고 나가 문 앞 유휴(2~5초)를 건너뛴다 | `ResidentExitDoorAction` |
/// | ⑤ | **동기 쿨다운** — 같은 문에서 같은 아침에 나온 무리끼리 서로 조우 쿨다운을 건다 | 이 클래스 |
///
/// ⑤가 새 개념이 아닌 것이 요점이다. <see cref="ResidentEncounterMemory"/>는 §7.1이
/// *"나란히 걷는 두 명이 조우 판정을 반복해 결국 붙는다"*를 막으려고 만든 것인데,
/// **같은 문에서 나온 둘은 구조적으로 딱 그 "나란히 걷는 두 명"이다.** 같은 문제라 같은 해법을 쓴다.
/// 표적도 정확하다 — 등장 직후 사교를 통째로 막는 것이 아니라 문 동기끼리만 막으므로,
/// 길에서 마주친 남과는 정상적으로 대화한다.
[AddComponentMenu("NorthLand/Resident/Resident Spawner")]
public class ResidentSpawner : MonoBehaviour
{
    [Header("군중")]
    [Tooltip("마을에 돌아다니는 주민 기준 수(§3.1의 N). 화면의 주민 수 = N − AssignedTotal.")]
    [Min(0)]
    [SerializeField] private int crowdSize = 24;

    [Tooltip("무작위로 섞어 쓸 주민 프리팹. 외형 다양성은 이 목록으로 확보한다(§2).")]
    [SerializeField] private GameObject[] prefabs;

    [Tooltip("비워 두면 배치 수를 빼지 않고 crowdSize 그대로 쓴다(주민 테스트 씬용).")]
    [SerializeField] private ManagementController management;

    [Header("등장")]
    [Tooltip("한 명씩 내보내는 간격(초). 0이면 동시에 나온다 — ②.")]
    [Min(0f)]
    [SerializeField] private float emergeInterval = 0.35f;

    [Tooltip("같은 문에서 같은 아침에 나온 무리끼리 거는 조우 쿨다운(초) — ⑤.")]
    [Min(0f)]
    [SerializeField] private float cohortCooldownSeconds = 45f;

    [Tooltip("문 위치를 NavMesh로 끌어올 때 허용하는 거리. 문이 지면에서 살짝 떠 있어도 붙인다.")]
    [Min(0.01f)]
    [SerializeField] private float spawnSnapDistance = 2f;

    /// 이 스포너가 만든 주민 전부(활성·비활성 모두). 밤에는 비활성으로 두었다가 아침에 다시 쓴다 —
    /// 매 아침 Instantiate/Destroy를 반복하면 30명 규모에서 GC가 눈에 띈다.
    private readonly List<Resident> pool = new List<Resident>();

    /// 이번 아침에 아직 내보내지 못한 대기열. (주민, 나올 문) 짝이다.
    private readonly List<Resident> pendingResidents = new List<Resident>();
    private readonly List<ResidentDoorPoint> pendingDoors = new List<ResidentDoorPoint>();

    /// 문 목록을 섞어 쓰기 위한 작업 버퍼.
    private readonly List<ResidentDoorPoint> doorBuffer = new List<ResidentDoorPoint>();

    /// 같은 문에서 나온 무리를 모으는 버퍼(⑤의 O(k²) 표시용). k는 보통 1~2다.
    private readonly List<Resident> cohortBuffer = new List<Resident>();

    private float emergeTimer;

    // ⚠ 구독을 OnEnable이 아니라 Start에서 한다. DayNightManager는 자기 Awake에서 Instance를 세우는데,
    //   스크립트 실행 순서가 정해져 있지 않아 OnEnable 시점에는 아직 null일 수 있다.
    //   모든 Awake는 어떤 Start보다 먼저 돌므로 Start에서는 항상 잡힌다.
    private void Start()
    {
        DayNightManager dayNight = DayNightManager.Instance;

        if (dayNight != null)
        {
            dayNight.OnDayToNight += HandleDayToNight;
            dayNight.OnNightToDay += HandleNightToDay;
        }

        // 첫 낮은 문에서 나오는 것이 아니라 마을에 이미 살고 있던 것으로 친다 — 웨이포인트 근처에 흩뿌린다.
        // 문에서 내보내면 게임 시작이 "아침에 다 같이 출근하는" 그림이 되어, 이미 돌아가던 마을처럼 보이지 않는다.
        SpawnInitialCrowd();
    }

    private void OnDestroy()
    {
        DayNightManager dayNight = DayNightManager.Instance;

        if (dayNight != null)
        {
            dayNight.OnDayToNight -= HandleDayToNight;
            dayNight.OnNightToDay -= HandleNightToDay;
        }
    }

    // LateUpdate에서 돈다 — BehaviorGraphAgent가 Update에서 그래프를 틱하므로, 거둬들이기를 뒤에 두면
    // "도착 표시 → 소멸"이 **같은 프레임**에 끝난다. Update에 두면 실행 순서에 따라 한 프레임 늦어지고,
    // 그 사이 귀가 노드가 한 번 더 돈다.
    private void LateUpdate()
    {
        CollectArrivedHome();
        DrainEmergeQueue();
    }

    // ── 밤낮 ─────────────────────────────

    /// 밤 전환. **주민에게 직접 지시하지 않는다** — 각자의 BT가 `ResidentIsNightCondition`을 보고
    /// 스스로 귀가 브랜치로 넘어간다(선점이 그 전환을 한 틱에 끝낸다).
    /// 여기서 하는 일은 아직 안 나간 대기열을 비우는 것뿐이다.
    private void HandleDayToNight()
    {
        pendingResidents.Clear();
        pendingDoors.Clear();
    }

    /// 아침 전환. 목표 인원만큼 대기열을 채우고, 이후 <see cref="Update"/>가 간격을 두고 내보낸다.
    private void HandleNightToDay()
    {
        RefillEmergeQueue();
    }

    // ── 인원 ─────────────────────────────

    /// 화면에 있어야 할 주민 수(§3.1). 배치된 인원은 건물 안에 있으므로 빠진다.
    private int TargetCount
    {
        get
        {
            int assigned = management != null ? management.AssignedTotal : 0;

            return Mathf.Max(0, crowdSize - assigned);
        }
    }

    private int ActiveCount
    {
        get
        {
            int count = 0;

            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && pool[i].gameObject.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }
    }

    // ── 등장 ─────────────────────────────

    /// 목표 인원만큼 대기열을 만든다. **문에 고르게 배분한다**(①).
    ///
    /// 문서 §3.3이 "아침에 나오는 곳이 전날 밤 들어간 곳과 같을 필요가 없다"고 열어 뒀으므로
    /// 자유롭게 배분할 수 있다. 섞은 문 목록을 라운드로빈으로 도는 것이 가장 고르다 —
    /// 매번 무작위로 고르면 우연히 한 문에 넷이 몰리는 아침이 나온다.
    private void RefillEmergeQueue()
    {
        pendingResidents.Clear();
        pendingDoors.Clear();

        int needed = TargetCount - ActiveCount;

        if (needed <= 0)
        {
            return;
        }

        if (ResidentDoorPointRegistry.CollectUsable(doorBuffer) == 0)
        {
            Debug.LogWarning("[주민] 씬에 쓸 수 있는 ResidentDoorPoint가 없어 아침 등장을 건너뜁니다. " +
                "빈 GameObject에 ResidentDoorPoint를 붙여 문 앞에 배치하세요.", this);

            return;
        }

        Shuffle(doorBuffer);

        for (int i = 0; i < needed; i++)
        {
            Resident resident = TakeFromPool();

            if (resident == null)
            {
                break;
            }

            pendingResidents.Add(resident);
            pendingDoors.Add(doorBuffer[i % doorBuffer.Count]);
        }

        // ⑤ 같은 문에 배정된 무리끼리 서로 쿨다운을 건다. 등장 **전에** 걸어 두어야
        // 첫 조우 판정이 돌기 전에 효력이 생긴다.
        MarkCohortCooldowns();

        emergeTimer = 0f;
    }

    /// 대기열을 간격(②)을 두고 하나씩 내보낸다.
    private void DrainEmergeQueue()
    {
        if (pendingResidents.Count == 0)
        {
            return;
        }

        emergeTimer -= Time.deltaTime;

        if (emergeTimer > 0f)
        {
            return;
        }

        emergeTimer = emergeInterval;

        Resident resident = pendingResidents[0];
        ResidentDoorPoint door = pendingDoors[0];

        pendingResidents.RemoveAt(0);
        pendingDoors.RemoveAt(0);

        if (resident == null || door == null)
        {
            return;
        }

        Emerge(resident, door);
    }

    /// 문 위치에 세우고 등장 상태로 표시한다. 직진(③)과 목적지 선지정(④)은 BT의 등장 브랜치가 한다.
    private void Emerge(Resident resident, ResidentDoorPoint door)
    {
        // ⚠ 활성화 **전에** 위치를 잡는다. 켜고 나서 옮기면 한 프레임 동안 이전 자리(대개 원점)에 보인다.
        PlaceOnNavMesh(resident, door.Position, door.Forward);

        resident.BeginEmerge(door);
        resident.gameObject.SetActive(true);

        // ⚠ 그래프를 처음부터 다시 돌린다.
        //
        // 비활성화는 그래프를 **끝내지 않고 멈추기만** 한다(BehaviorGraphAgent에 Restart가 공개 API로
        // 있는 것이 그 증거다). 밤에 귀가 노드가 Running인 채로 거둬졌으므로, 그냥 켜면 그 노드가
        // **어젯밤 상태 그대로 이어진다** — 어제 문을 향해 뛰거나 그 자리에서 도착 판정이 서서
        // 나오자마자 다시 사라진다. 재사용하는 오브젝트는 깨끗한 상태에서 시작해야 한다.
        var graphAgent = resident.GetComponent<Unity.Behavior.BehaviorGraphAgent>();

        if (graphAgent != null)
        {
            graphAgent.Restart();
        }
    }

    // ── 소멸 ─────────────────────────────

    /// 문에 도착한 주민을 거둔다.
    ///
    /// **BT 노드가 직접 비활성화하지 않는 이유**: 노드는 `BehaviorGraphAgent.Update` 위에서 도는데,
    /// 거기서 자기 GameObject를 끄면 그래프가 자기 Update 스택 위에서 꺼진다. 표시만 남기고
    /// 프레임 끝에 여기서 처리하면 그 위험이 없다.
    private void CollectArrivedHome()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            Resident resident = pool[i];

            if (resident != null && resident.gameObject.activeSelf && resident.HasArrivedHome)
            {
                resident.gameObject.SetActive(false);
            }
        }
    }

    // ── 생성 ─────────────────────────────

    /// 첫 낮의 군중. 웨이포인트 반경 안에 흩뿌린다 — 이미 살고 있던 마을로 보이게 한다.
    private void SpawnInitialCrowd()
    {
        int target = TargetCount;
        bool warnedNoWaypoint = false;

        for (int i = 0; i < target; i++)
        {
            Resident resident = TakeFromPool();

            if (resident == null)
            {
                break;
            }

            if (TryGetScatterPoint(out Vector3 point))
            {
                PlaceOnNavMesh(resident, point, RandomFlatDirection());
            }
            else
            {
                // 웨이포인트가 없으면 스포너 자리에 세운다. 원점에 몰리는 것보다는 낫고,
                // 어차피 웨이포인트가 없으면 산책 자체가 돌지 않는다(뽑기 노드가 따로 경고한다).
                if (!warnedNoWaypoint)
                {
                    warnedNoWaypoint = true;
                    Debug.LogWarning("[주민] 웨이포인트가 없어 초기 군중을 스포너 위치에 세웁니다.", this);
                }

                PlaceOnNavMesh(resident, transform.position, RandomFlatDirection());
            }

            resident.gameObject.SetActive(true);
        }
    }

    private static Vector3 RandomFlatDirection() =>
        Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

    /// 비활성 주민을 재사용하고, 없으면 새로 만든다.
    ///
    /// ⚠ **대기열에 이미 잡힌 주민을 다시 내주면 안 된다.** 아침 대기열은 만들 때가 아니라 순차로
    ///   내보낼 때(②) 활성화되므로, 활성 여부만 보면 **같은 주민이 24번 뽑힌다** — 실제로 그렇게 돌아서
    ///   한 명이 문 사이를 0.35초마다 순간이동하고 나머지는 영영 안 나왔다.
    private Resident TakeFromPool()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            Resident candidate = pool[i];

            if (candidate != null && !candidate.gameObject.activeSelf && !pendingResidents.Contains(candidate))
            {
                return candidate;
            }
        }

        return Create();
    }

    private Resident Create()
    {
        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogWarning("[주민] 스포너에 프리팹이 지정되지 않아 주민을 만들 수 없습니다.", this);
            return null;
        }

        GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];

        if (prefab == null)
        {
            return null;
        }

        // 비활성으로 만들어 두고 배치가 끝난 뒤에 켠다 — Awake/OnEnable이 원점에서 도는 것을 막는다.
        GameObject instance = Instantiate(prefab, transform);
        instance.SetActive(false);

        var resident = instance.GetComponent<Resident>();

        if (resident == null)
        {
            Debug.LogWarning($"[주민] 프리팹 '{prefab.name}'에 Resident 컴포넌트가 없습니다.", this);
            Destroy(instance);

            return null;
        }

        pool.Add(resident);

        return resident;
    }

    // ── 배치 헬퍼 ─────────────────────────────

    /// NavMesh 위에 세운다. `NavMeshAgent`는 NavMesh 밖에서 목적지 지정이 조용히 무시되므로,
    /// 스폰 위치가 어긋나면 그 주민은 아무 데도 가지 못한 채 서 있는다.
    private void PlaceOnNavMesh(Resident resident, Vector3 position, Vector3 facing)
    {
        Vector3 target = NavMesh.SamplePosition(position, out NavMeshHit hit, spawnSnapDistance, NavMesh.AllAreas)
            ? hit.position
            : position;

        var navAgent = resident.GetComponent<NavMeshAgent>();

        // Warp는 비활성 Agent에 통하지 않는다. 켜기 전이라 transform을 직접 옮기고,
        // 활성화 시 Agent가 그 자리에서 NavMesh에 붙는다.
        resident.transform.position = target;

        Vector3 flat = new Vector3(facing.x, 0f, facing.z);

        if (flat.sqrMagnitude > 0.0001f)
        {
            resident.transform.rotation = Quaternion.LookRotation(flat.normalized);
        }

        if (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh)
        {
            navAgent.Warp(target);
        }
    }

    private static bool TryGetScatterPoint(out Vector3 point)
    {
        point = Vector3.zero;

        return ResidentWaypointRegistry.TryGetRandomWaypoint(out ResidentWaypoint waypoint) &&
            waypoint.TryGetRandomPoint(out point);
    }

    /// 같은 문에 배정된 무리끼리 서로 쿨다운을 건다(⑤). 양쪽 표에 다 남긴다 —
    /// 한쪽만 기억하면 반대쪽이 곧바로 말을 건다(§7.1과 같은 이유).
    private void MarkCohortCooldowns()
    {
        if (cohortCooldownSeconds <= 0f)
        {
            return;
        }

        for (int d = 0; d < pendingDoors.Count; d++)
        {
            ResidentDoorPoint door = pendingDoors[d];

            // 앞에서 이미 무리를 만든 문이면 건너뛴다.
            if (IndexOfDoorBefore(door, d) >= 0)
            {
                continue;
            }

            cohortBuffer.Clear();

            for (int i = d; i < pendingDoors.Count; i++)
            {
                if (pendingDoors[i] == door && pendingResidents[i] != null)
                {
                    cohortBuffer.Add(pendingResidents[i]);
                }
            }

            // 혼자 나오는 문은 표시할 상대가 없다 — ①이 성공한 경우다.
            if (cohortBuffer.Count < 2)
            {
                continue;
            }

            for (int i = 0; i < cohortBuffer.Count; i++)
            {
                for (int j = 0; j < cohortBuffer.Count; j++)
                {
                    if (i != j)
                    {
                        cohortBuffer[i].Encounters.Mark(cohortBuffer[j], cohortCooldownSeconds);
                    }
                }
            }
        }
    }

    private int IndexOfDoorBefore(ResidentDoorPoint door, int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            if (pendingDoors[i] == door)
            {
                return i;
            }
        }

        return -1;
    }

    private static void Shuffle(List<ResidentDoorPoint> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ── 디버그 ─────────────────────────────
    //
    // 밤낮 전환을 손으로 돌려 보기 위한 것. **실제 경로를 그대로 탄다** —
    // DayNightManager를 직접 호출하므로 이벤트·CurrentPhase·BT 조건이 전부 정상적으로 움직인다.
    // 여기서 HandleDayToNight를 바로 부르면 CurrentPhase가 낮인 채라 귀가 조건이 서지 않는다.

    [ContextMenu("밤으로 (귀가 시작)")]
    private void DebugGoNight()
    {
        if (DayNightManager.Instance == null)
        {
            Debug.LogWarning("[주민] 씬에 DayNightManager가 없어 밤 전환을 시험할 수 없습니다.", this);
            return;
        }

        DayNightManager.Instance.EndDay();
    }

    [ContextMenu("아침으로 (등장 시작)")]
    private void DebugGoMorning()
    {
        if (DayNightManager.Instance == null)
        {
            Debug.LogWarning("[주민] 씬에 DayNightManager가 없어 아침 전환을 시험할 수 없습니다.", this);
            return;
        }

        DayNightManager.Instance.EndNight();
    }
}
