using UnityEngine;

/// 주민 1명의 신원·상태(#276). 대화 중 여부 · 사교성 · 상대별 쿨다운을 든다.
///
/// <see cref="ResidentAgent"/>와 병존한다 — <c>EnemyAgent</c>가 <c>Enemy</c>와 나란히 붙는 것과 같은 구성이다.
/// 역할 분담이 그 선례와 같다:
///  · 이 컴포넌트 = **상태의 정본**. 다른 시스템(드래그 · 배치 · 밤낮)이 조회할 공개 상태를 여기 둔다
///  · <see cref="ResidentAgent"/> = **BT 노드 전용 파사드**. 노드는 여기 직접 닿지 않고 파사드를 경유한다
///
/// 왜 파사드에 상태를 넣지 않았는가: <see cref="ResidentAgent"/>는 "무상태 파사드"로 선언돼 있고
/// (위치는 NavMeshAgent, 재생 상태는 Animator가 소유) 여기에 세션을 얹으면 그 경계가 무너진다.
/// 문서(Docs/ManagementArea/Resident.md §1)도 이 컴포넌트를 따로 예약해 뒀다.
[AddComponentMenu("NorthLand/Resident/Resident")]
[RequireComponent(typeof(ResidentAgent))]
public class Resident : MonoBehaviour
{
    [Tooltip("사교성 추첨 범위. 1이 기준이고 낮으면 조용한 주민, 높으면 말 많은 주민이 된다. " +
             "개체마다 Awake에서 이 구간에서 뽑는다.")]
    [SerializeField] private Vector2 sociabilityRange = new Vector2(0.6f, 1.4f);

    private ResidentAgent agent;

    /// 대화 성립 확률에 곱해지는 개체 계수(§7.1). **개체마다 다르다.**
    ///
    /// 문서는 이 값을 Blackboard에 두라고 적었지만, Blackboard의 **저작 기본값은 세 프리팹이 공유**하므로
    /// 그것만으로는 개체차가 생기지 않는다. 개체차를 만들려면 어차피 런타임에 써야 하고, 그렇다면
    /// 인스펙터로 범위를 저작할 수 있는 여기가 맞다. "개체차를 코드가 아니라 데이터로 흡수한다"는
    /// 원칙(§7)은 그대로 지켜진다 — 갈리는 것은 값이고 그래프는 하나다.
    public float Sociability { get; private set; } = 1f;

    /// 참가 중인 대화 세션. 없으면 null.
    ///
    /// **세션 객체가 소유권을 갖고 주민은 참조만 든다**(§7.1) — 주민이 세션을 소유하면 그 주민이
    /// 사라질 때 세션이 통째로 유실되어, 남은 쪽이 "상대가 빠졌다"를 감지할 수 없게 된다.
    public ResidentConversation Conversation { get; set; }

    /// 상대별 재시도 쿨다운. 실패한 조우와 끝난 대화를 같은 표에 기록한다(§7.1 재진입 방지).
    public ResidentEncounterMemory Encounters { get; } = new ResidentEncounterMemory();

    /// BT 파사드. 대화 상대를 그 자리에 세울 때처럼 **상대의 몸을 만져야 하는** 경우에 쓴다.
    public ResidentAgent Agent => agent;

    /// 춤추는 중인가(R5).
    ///
    /// 춤은 **§10 공연의 선행 형태**다. 공연이 들어오면 이 자리에 `ResidentPerformance` 세션 참조가
    /// 나란히 붙고, 아래 <see cref="IsBusy"/>가 그것도 함께 흡수한다 — 지금 bool 하나인 이유는 춤이
    /// 혼자 하는 행위라 참가자를 묶을 객체가 필요 없기 때문이지, 구조가 다르기 때문이 아니다.
    public bool IsDancing { get; private set; }

    /// 문에서 막 나와 아직 대열을 벗어나지 못한 상태(R9 등장 · §3.2 퇴장 유예).
    /// 어느 문에서 나왔는지를 들고 있어야 그 문의 +Z로 직진할 수 있다.
    public ResidentDoorPoint EmergingFrom { get; private set; }

    public bool IsEmerging => EmergingFrom != null;

    /// 밤에 문 앞에 도착했다(R8 귀가). **스스로 사라지지 않고 표시만 남긴다** —
    /// BT 노드가 도는 도중에 자기 GameObject를 비활성화하면 그래프가 자기 Update 위에서 꺼진다.
    /// 실제 소멸은 <see cref="ResidentSpawner"/>가 프레임 끝에 처리한다.
    public bool HasArrivedHome { get; private set; }

    /// 다른 행위에 매여 있는가. "무엇에" 매였는지는 소비처가 알 필요가 없다.
    ///
    /// 이 한 줄이 **춤추는 주민에게 말을 걸 수 없다**를 보장한다(공연도 같은 규칙이 될 것이다).
    /// 조건을 소비처마다 나열하면 행위가 늘 때마다 모든 소비처를 고쳐야 한다.
    ///
    /// 등장 중·귀가 완료도 여기 들어온다 — **문에서 나오는 중인 주민에게 말을 걸 수 없다**(§11.11 ②·③).
    public bool IsBusy => Conversation != null || IsDancing || IsEmerging || HasArrivedHome;

    /// 말을 걸어도 되는 상태인가. 이후 밤·들려 있음 조건이 여기 더해진다(§3.3 · §8).
    public bool IsAvailableForConversation => !IsBusy && isActiveAndEnabled;

    /// 춤이 **끝까지 가지 못하고 끊겼는가**(누군가 다가와서). 반응 노드가 읽고 소비한다.
    ///
    /// 왜 플래그가 필요한가: 선점이 춤 브랜치를 중단시키면 그 즉시 <see cref="IsDancing"/>이 꺼지므로,
    /// 한 틱 뒤에 도는 반응 브랜치는 **자기가 왜 불렸는지 알 수 없다.** 감시 조건은 이미 거짓이 되어 있다.
    /// "방금 춤이 끊겼다"는 사실을 남기는 것이 이 플래그의 전부다.
    public bool DanceInterrupted { get; private set; }

    /// 춤 시작·종료. BT 노드가 <c>OnStart</c>/<c>OnEnd</c> 짝으로 부른다 —
    /// <c>OnEnd</c>는 중단으로 끝난 경우에도 지나가는 유일한 경로라, 끄는 것을 빠뜨리면
    /// **그 주민은 이후 영원히 대화 상대가 되지 못한다.**
    public void BeginDance()
    {
        IsDancing = true;

        // 지난 중단 기록은 새 춤이 시작되는 순간 무의미해진다. 반응 노드가 소비하지 못한 채 남아 있으면
        // 엉뚱한 시점에 반응이 터진다.
        DanceInterrupted = false;
    }

    /// <paramref name="interrupted"/>가 참이면 "끝까지 못 췄다"를 남긴다.
    public void EndDance(bool interrupted)
    {
        IsDancing = false;
        DanceInterrupted = interrupted;
    }

    /// 중단 기록을 읽고 지운다. 반응 브랜치가 한 번만 반응하도록 **읽는 쪽이 소비한다.**
    public bool ConsumeDanceInterrupted()
    {
        bool value = DanceInterrupted;
        DanceInterrupted = false;

        return value;
    }

    // ── 등장 · 귀가 ─────────────────────────────

    /// 스포너가 아침에 문에서 꺼내며 부른다. 등장 브랜치가 이 문의 +Z로 직진한 뒤 스스로 푼다.
    public void BeginEmerge(ResidentDoorPoint from)
    {
        EmergingFrom = from;
        HasArrivedHome = false;
    }

    public void EndEmerge() => EmergingFrom = null;

    /// 밤에 문 앞에 도착했다. 스포너가 다음 프레임에 거둬 간다.
    public void MarkArrivedHome() => HasArrivedHome = true;

    private void Awake()
    {
        agent = GetComponent<ResidentAgent>();

        float min = Mathf.Min(sociabilityRange.x, sociabilityRange.y);
        float max = Mathf.Max(sociabilityRange.x, sociabilityRange.y);
        Sociability = max > min ? Random.Range(min, max) : min;
    }

    private void OnEnable() => ResidentRegistry.Register(this);

    private void OnDisable()
    {
        ResidentRegistry.Unregister(this);

        // ⚠ 세션을 해산시키지 않는다. 참조만 놓는다.
        //   여기서 세션을 끝내면 남은 참가자가 "상대가 빠졌다"를 알아차릴 근거가 사라져 R7 놀람이
        //   나오지 않는다. 세션은 나를 여전히 참가자로 들고 있고, 남은 쪽이 다음 틱에
        //   ResidentConversation.HasLostParticipant로 이 상황을 읽는다.
        Conversation = null;

        // 춤은 반대다 — 혼자 하는 행위라 남에게 알릴 것이 없다. 켜진 채로 두면 다시 활성화됐을 때
        // 춤추지도 않으면서 대화 후보에서 빠진다.
        IsDancing = false;
        DanceInterrupted = false;

        // 밤에 거둬들일 때 여기를 지난다. 남겨 두면 아침에 다시 켜자마자 "이미 귀가함"으로 읽혀
        // 그 자리에서 다시 사라진다.
        EmergingFrom = null;
        HasArrivedHome = false;
    }
}
