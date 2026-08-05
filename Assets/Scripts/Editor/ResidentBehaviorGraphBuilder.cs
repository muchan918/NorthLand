using System.Collections.Generic;
using System.Linq;
using Unity.Behavior;
using Unity.Behavior.GraphFramework;
using UnityEditor;
using UnityEngine;

// 주민 BT 그래프(ResidentBehavior.asset)를 코드로 저작한다(#276).
//
// 왜 코드로 만드는가: 그래프를 손으로 그리면 값 조정 하나에도 에디터를 열어야 하고, 무엇이 왜 그 값인지가
// 에셋 안에만 남아 리뷰가 안 된다. 여기서 만들면 구조와 수치가 diff에 그대로 드러난다.
//
// com.unity.behavior의 저작 API는 전부 internal이지만 패키지가
// [assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]를 선언하고 있어, 이 프로젝트에 asmdef이 없는 한
// Assets/Scripts/Editor/에서 리플렉션 없이 직접 닿는다. (프로젝트에 asmdef을 도입하면 이 파일이 깨진다.)
// SelectorComposite · ObserverAbortModifier · ConditionModel · ConditionUtility 전부 그 경로로 쓴다.
//
// ⚠ NodeRegistry는 Unity.Behavior와 Unity.Behavior.GraphFramework 양쪽에 있어 그냥 쓰면 CS0104가 난다.
//   반드시 정규화할 것.
public static class ResidentBehaviorGraphBuilder
{
    public const string GraphPath = "Assets/Behavior/ResidentBehavior.asset";

    // 상태 이름 대조용. 노드에 넘기는 이름이 여기 실재하는지 자기검사가 확인한다 —
    // 문자열이라 컴파일이 잡아 주지 않는다.
    private const string ControllerPath = "Assets/Imported/@NorthLand/Animations/Resident/Resident.controller";

    // ── R1 유휴 · R2 산책 · R15 휴식 ─────────────────────────────

    // R1 유휴 — 도착해서 다음 목적지를 정하기 전까지 머무는 시간.
    // 고정값이면 도착 시각이 비슷한 주민들이 같은 박자로 동시에 출발하므로 구간에서 뽑는다.
    private const float IdleMinSeconds = 2f;

    private const float IdleMaxSeconds = 5f;

    // 이동 상한(초). 도달 불가능한 지점이 잡혔을 때 브랜치가 멎는 것을 막는 안전장치다.
    // 목적지가 웨이포인트가 되면서 이동 거리가 맵 크기에 비례하므로, 예전(제자리 근처 산책)보다 넉넉해야 한다.
    private const float MoveTimeoutSeconds = 60f;

    // 한 번에 걷는 구간(초). 도착 전이라도 여기서 브랜치를 끊어 휴식 판정 자리를 만든다.
    // 짧으면 판정이 잦아 확률을 낮춰야 하고, 길면 짧은 여정에서 한 번도 안 굴려진다.
    //
    // 이 값이 **조우 판정 주기도 함께 정한다** — Selector가 한 바퀴 돌 때마다 대화 시도가 1회 굴려지므로,
    // 구간을 줄이면 휴식과 조우 확률을 둘 다 낮춰야 한다.
    private const float MoveSegmentSeconds = 4f;

    // 0.25로 시작했다가 실제로 걷는 것을 보고 0.15로 낮췄다 — 너무 자주 서면 산책이 아니라
    // 자꾸 멈칫하는 그림이 된다.
    private const float RestChance = 0.15f;

    // 남은 거리가 이보다 짧으면 쉬지 않는다. 속도 1.5 기준 약 4초 거리 — "곧 도착"의 경계다.
    private const float RestMinRemainingDistance = 6f;

    // 휴식 길이. 고정값이면 박자가 맞아떨어지므로 구간에서 뽑는다.
    private const float RestMinSeconds = 1.5f;

    private const float RestMaxSeconds = 3.5f;

    // ── R3 인사 · R4 수다 · R7 놀람 · R12 웃음 ─────────────────────────────

    // 조우 반경. **넓게 잡는 것이 연출의 전제다.**
    //
    // 판정은 Selector 한 바퀴에 1회씩 도는 **표본**이라(연속 감시가 아니다) 반경이 좁으면 롤이 성공하기
    // 전에 두 주민이 이미 걸어와 부딪힌다 — 그 뒤에 벌어지므로 "부딪혔다가 물러나서 인사"가 된다.
    // 6.4면 마주 걸어오는 쌍(초당 최대 3 접근)도 판정 주기(4초) 안에 여유 있게 걸린다.
    //
    // **대화 거리(4)보다 커야 한다.** 조우 반경이 그보다 좁으면 다가가기가 좁히는 게 아니라 벌리는
    // 동작만 하게 되어 "멀리서 알아보고 다가온다"가 성립하지 않는다. 3.5 → 6 → 6.4로 실측 조정했다.
    private const float EncounterRadius = 6.4f;

    // 조우 1회당 대화 성립 확률(사교성 평균이 곱해진다).
    //
    // 반경을 3.5 → 6으로 넓히면서 함께 낮췄다. 반경이 넓으면 후보가 거의 항상 있어 **롤이 매 주기 도는
    // 것과 같아진다** — 주민 1명이 4~9초에 한 번 굴리므로 30명이면 초당 3~7회다. 대화 1건이 인사·다가감·
    // 수다 2~4턴·헤어짐으로 20~35초 동안 두 명을 붙잡으니, 확률이 0.1을 넘으면 정상 상태에서 마을 절반이
    // 대화 중이 되어 산책·춤·유휴가 화면에서 사라진다(§7.1이 경고하는 그림).
    // **여전히 산술로 잡은 값이고 실제로 보고 조정할 대상이다.**
    private const float EncounterChance = 0.05f;

    // 확률 판정에 실패한 상대를 다시 후보로 올리기까지의 시간(초).
    // 이것이 없으면 나란히 걷는 두 명이 구간마다 다시 굴려져 "확률로 거른다"가 무너진다.
    private const float EncounterFailCooldownSeconds = 8f;

    // 해산 후 같은 상대와 다시 성립하지 않는 시간(초). 없으면 두 명이 영원히 인사만 한다.
    private const float ConversationDisbandCooldownSeconds = 30f;

    // 주고받을 턴 수. **시간이 아니라 턴 수로 정한다** — 시간으로 끊으면 Talking_1(10.27초)이 뽑힌
    // 마지막 턴이 중간에 잘린다(§7.2). 문서에 T가 정의돼 있지 않아 여기서 정한다.
    private const int MinTurns = 2;

    private const int MaxTurns = 4;

    // 수다 클립 상태 이름. **가중치는 같은 이름을 여러 번 넣어 표현한다** —
    // Talking_1이 10.27초로 나머지(3.93 / 3.77)의 2.6배라 균등하게 두면 한 사람이 10초를 독점하는 턴이
    // 자주 나온다. 아래 구성이면 Talking_1이 1/5로 나오는 "가끔 있는 긴 이야기"가 된다.
    private static readonly List<string> TalkStates = new List<string>
    {
        "Talking_2", "Talking_3", "Talking_2", "Talking_3", "Talking_1",
    };

    // 이야기하는 동안 두 주민 사이의 거리. **1.8 → 2.4 → 4**(실측으로 확정).
    //
    // NavMeshAgent radius가 0.3씩이라 물리적으로는 0.6까지 붙지만, **Marshie는 치비 비율이라 머리가
    // 몸통보다 넓다** — 발 기준 거리로 잡으면 몸은 떨어져 있는데 머리가 부딪힌다. 두 번을 늘리고서야
    // 안 겹쳤다는 것은 발 기준과 눈에 보이는 실루엣의 차이가 예상보다 크다는 뜻이다.
    private const float ConversationStandDistance = 4f;

    // 다가가기 상한(초). 조우 반경 6에서 2.4까지 좁히면 각자 약 1.8을 걷고(속도 1.5 → 1.2초),
    // 회피로 돌아가는 경우까지 감안한 값이다.
    private const float ApproachTimeoutSeconds = 6f;

    // Resident.controller의 상태 이름과 일치해야 한다. 틀리면 노드가 상한(1.5초) 뒤에 경고를 남긴다.
    private const string GreetState = "Wave";

    private const string LaughState = "Laughing";

    private const string SurprisedState = "Surprised";

    // 상태 전환 크로스페이드(초). Mixamo 대화 클립은 시작·끝이 중립 서 있는 자세라 짧게 이어도 튀지 않는다.
    private const float CrossFadeSeconds = 0.15f;

    // 마주 봤다고 인정하는 각도. NavMeshAgent의 angularSpeed 360 기준 180° 회전이 0.5초라
    // 이 값이 인사 시작을 눈에 띄게 늦추지 않는다.
    private const float FaceToleranceDegrees = 15f;

    // R12 웃음 — 청자가 한 턴에 웃을 확률. 턴당 1회만 굴린다.
    private const float LaughChance = 0.35f;

    // 턴의 몇 %가 지난 뒤부터 웃어도 되는지. 턴 시작 직후에 웃으면 아직 아무 말도 안 들었는데 웃는 꼴이다.
    private const float LaughAfterTurnFraction = 0.3f;

    // 웃은 뒤 쉬는 턴 수. 1이면 다음 청자 턴은 건너뛴다.
    private const int LaughTurnCooldown = 1;

    // 상대의 합류를 기다리는 상한(초). 선점이 정상 동작하면 한 틱에 합류하므로 이 값은 안전장치다 —
    // 선점이 죽으면 상대의 이동 구간(4초)만큼 걸릴 수 있어 그보다 넉넉해야 한다.
    private const float ConversationPendingTimeoutSeconds = 8f;

    // 대화 브랜치 전체의 상한(초). 최악(4턴 전부 Talking_1)이 약 43초라 그보다 넉넉하게 둔다.
    private const float ConversationMaxSeconds = 120f;

    // ── R5 춤 ─────────────────────────────

    // 판정 1회당 춤출 확률. **0.05 → 0.10 → 0.03**(실측으로 확정).
    //
    // ⚠ 반경을 넓히면 확률도 올려야 할 것 같지만 **반대다.** "혼자"라는 조건은 한 번 성립하면 그 상태로
    //   여러 주기가 지나간다(빈 구역을 가로질러 걷는 동안 계속 혼자다). 즉 판정은 드물게 열리는 것이 아니라
    //   **열린 동안 연달아 굴려진다.** 그래서 반경이 넓어질수록 한 번의 고독 구간이 길어져 확률을 낮춰야 한다.
    //   산술로 예측한 방향과 실측이 반대였던 자리다.
    private const float DanceChance = 0.03f;

    // "혼자"를 판정하는 반경. 6(조우 반경과 동일) → 15 → **30**(실측으로 확정).
    //
    // 6은 "말 걸 사람이 없다"는 뜻이지 "아무도 안 본다"는 뜻이 아니다. 남이 보는 앞에서 춤추지 않으려면
    // **볼 수 있는 거리**로 잡아야 하고, 오쏘그래픽 경영 카메라는 최대 축소에서 세로 70유닛을 담으므로
    // 30은 여전히 화면 안이다.
    private const float DanceSoloRadius = 30f;

    // 춤을 중단시키는 반경. 시작 조건과 같은 값이라 **"아무도 없을 때 시작하고, 누가 들어오면 멈춘다"**가
    // 문자 그대로 성립한다.
    //
    // 상수를 따로 둔 이유는 이력(hysteresis)이 필요해질 수 있기 때문이다 — 같은 값이면 경계에서
    // 멈췄다 다시 추는 것이 반복될 수 있다. 그때 이 값만 줄이면 "멀리서 오면 계속 추고, 가까이 와야 멈춘다"가 된다.
    private const float DanceInterruptRadius = 30f;

    // 이 수를 넘으면 춤추지 않는다(중단 판정도 같은 기준). 0 = 반경 안에 아무도 없어야 한다.
    //
    // 완전히 혼자로 두는 것이 중요하다 — 그래야 "춤추는 중에 사람이 들어오면 부끄러워한다"는 후속 연출이
    // 성립한다. 처음부터 옆에 사람이 있었으면 부끄러워할 계기가 없다.
    private const int DanceMaxNeighbors = 0;

    private const string DanceState = "Dance";

    // 몇 바퀴 돌고 끝낼지. 클립이 2.30초 루프라 2~4바퀴면 4.6~9.2초다.
    // 시간이 아니라 바퀴로 세야 동작 중간에 잘리지 않는다.
    private const int DanceMinLoops = 2;

    private const int DanceMaxLoops = 4;

    // 춤 브랜치 상한(초). 최악(4바퀴 = 9.2초)보다 넉넉하게.
    private const float DanceMaxSeconds = 20f;

    // ── R8 귀가 · R9 등장 ─────────────────────────────

    private const string RunState = "Run";

    // 귀가할 때의 이동속도 배수. 2로 시작했다가 **5로 올렸다**(실측). 기준 1.5 × 5 = 7.5다.
    //
    // "서둘러 들어간다"가 아니라 **도망치듯 들어간다**에 가까운 값인데, 밤은 전투 페이즈라 그 편이 맞다.
    // 절대값이 아니라 배수인 이유는 프리팹에서 기준 속도를 조정해도 따라오게 하기 위해서다.
    //
    // ⚠ 대가: 이 속도에서 Running(1.10초 루프)의 보폭이 맞지 않아 발이 미끄러진다.
    //   잡으려면 컨트롤러 Run 상태의 speed 배수를 함께 올려야 한다 — 아직 안 했다(§9 TODO).
    private const float GoHomeSpeedFactor = 5f;

    // 문에 도착했다고 볼 거리. 1.2로 시작했다가 **3으로 넓혔다**(실측).
    //
    // 두 가지가 겹친다. 문 앞은 여럿이 몰리는 자리라 회피에 밀려 stoppingDistance 안으로 못 들어가는
    // 경우가 있고, **속도 7.5에서는 한 프레임에 0.12씩 움직여** 좁은 판정을 스쳐 지나간다.
    // 도착 판정이 서지 않으면 그 주민만 문 앞을 맴돌다 상한(90초)까지 남는다.
    private const float GoHomeArriveDistance = 3f;

    // 귀가 상한(초). 맵을 가로질러도 닿는 값이어야 한다 — 속도 3(1.5 × 2)에서 90초면 270유닛이다.
    private const float GoHomeMaxSeconds = 90f;

    // 문 앞에서 +Z로 직진하는 거리(§3.2의 D). 건물 콜라이더 크기에 종속되므로 실제 에셋이 깔린 뒤에 조정한다.
    private const float ExitDistance = 3f;

    // 직진 상한(초). 문 앞이 막혀 있어도 여기서 풀어 준다 — 안 그러면 그 주민은 영영 합류하지 못한다.
    private const float ExitMaxSeconds = 6f;

    // ── 브랜치 우선순위 = 노드의 X 좌표 ─────────────────────────────
    //
    // ⚠ 컴포지트의 자식 순서는 코드상 연결 순서가 아니라 **노드 Position.x 오름차순**으로 결정된다
    //   (GraphAssetProcessor.GetSortedConnections). 즉 왼쪽이 높은 우선순위다.
    //   X를 명시적으로 벌려 놓지 않으면 브랜치 우선순위가 조용히 뒤바뀐다.
    // 우선순위 근거(위 → 아래):
    //  · **밤 귀가가 가장 위** — 해가 지면 대화든 춤이든 전부 끊고 들어간다
    //  · 등장 유예가 그다음 — 문에서 나오는 중에는 아무것도 평가하지 않는다(§3.2)
    //  · 대화 — 이미 성립한 세션은 그 아래 무엇보다 먼저 이어져야 한다
    //  · 목격 반응이 춤보다 위 — LowerPriority는 아래만 끊으므로 춤을 끊으려면 위에 있어야 한다
    //  · 춤이 조우보다 위 — 춤추는 동안 말을 걸러 나가지 않는다(§10 공연과 같은 규칙). 반대쪽,
    //    즉 "춤추는 사람에게 남이 말을 거는 것"은 Resident.IsBusy가 막는다
    //  · 산책이 맨 아래 — 아무것도 해당 없을 때의 기본
    private const float GoHomeBranchX = 0f;

    private const float ExitBranchX = 360f;

    private const float ConversationBranchX = 720f;

    private const float ReactionBranchX = 1080f;

    private const float DanceBranchX = 1440f;

    private const float EncounterBranchX = 1800f;

    private const float StrollBranchX = 2160f;

    // 춤이 끊긴 뒤의 반응 클립. **지금은 비어 있다** — 중단만 하고 아무것도 재생하지 않는다.
    // 부끄러움 클립을 받으면 { "Surprised", "Embarrassed" }로 채우기만 하면 놀람 → 부끄러움이 순서대로 돈다.
    private static readonly List<string> ReactionStates = new List<string>();

    private const float ReactionMaxSeconds = 10f;

    [MenuItem("NorthLand/Resident/Rebuild Behavior Graph")]
    private static void RebuildFromMenu()
    {
        Debug.Log(Build(GraphPath));
    }

    /// 지금 그래프에 실려 있는 값을 전부 찍는다.
    ///
    /// **에디터에서 손으로 튜닝한 값을 상수로 회수하기 위한 도구다.** 그래프 손 편집은 다음 재빌드에
    /// 사라지므로(§11.6), 재빌드 전에 이걸로 값을 뽑아 위 상수에 옮겨 적어야 한다.
    ///
    /// `unity-cli exec`로는 이 일을 할 수 없다 — exec 코드는 `InternalsVisibleTo` 대상이 아닌 별도
    /// 어셈블리라 `SelectorComposite` 같은 패키지 internal에 닿지 못한다(§11.7).
    [MenuItem("NorthLand/Resident/Dump Behavior Graph Values")]
    private static void DumpFromMenu()
    {
        var runtime = AssetDatabase.LoadAllAssetsAtPath(GraphPath).OfType<BehaviorGraph>().FirstOrDefault();
        Node root = runtime != null && runtime.RootGraph != null ? runtime.RootGraph.Root : null;

        if (root == null)
        {
            Debug.LogError("[ResidentBehaviorGraphBuilder] 컴파일된 그래프를 찾지 못했다.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[주민 BT 현재 값] {GraphPath}");
        DumpNode(root, sb, 0);

        Debug.Log(sb.ToString());
    }

    private static void DumpNode(Node node, System.Text.StringBuilder sb, int depth)
    {
        if (node == null)
        {
            return;
        }

        string indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}{node.GetType().Name}");

        DumpFields(node, sb, depth + 1);

        // 관찰자의 조건도 파라미터를 든다(예: 춤 중단 반경).
        if (node is IObserverAbort observer && observer.Conditions != null)
        {
            foreach (Condition condition in observer.Conditions)
            {
                sb.AppendLine($"{indent}  [조건] {condition.GetType().Name}");
                DumpFields(condition, sb, depth + 2);
            }
        }

        switch (node)
        {
            case Composite composite:
                foreach (Node child in composite.Children)
                {
                    DumpNode(child, sb, depth + 1);
                }

                break;

            case Modifier modifier:
                DumpNode(modifier.Child, sb, depth + 1);
                break;
        }
    }

    /// `BlackboardVariable<T>` 필드를 전부 찍는다. 노드마다 손으로 나열하지 않는 이유는
    /// 파라미터가 늘어날 때 이 도구가 따라오지 않으면 **조용히 빠진 값이 생기기** 때문이다.
    private static void DumpFields(object target, System.Text.StringBuilder sb, int depth)
    {
        string indent = new string(' ', depth * 2);

        foreach (System.Reflection.FieldInfo field in target.GetType().GetFields())
        {
            if (field.GetValue(target) is not BlackboardVariable variable)
            {
                continue;
            }

            object value = variable.ObjectValue;

            string text = value is System.Collections.IEnumerable list and not string
                ? "[" + string.Join(", ", list.Cast<object>()) + "]"
                : value?.ToString() ?? "null";

            sb.AppendLine($"{indent}{field.Name} = {text}");
        }
    }

    // 멱등하다 — 수치를 고치고 다시 돌리면 그대로 반영된다.
    // 대신 에디터에서 손으로 고친 내용은 사라진다. 그래프를 손으로 편집하기 시작하면 이 빌더를 버려야 한다.
    //
    // ⚠ 에셋을 지우고 다시 만들면 안 된다. 파일 GUID는 경로가 같아 유지되지만 **서브에셋 fileID가 새로
    //   발급되어**, 런타임 그래프를 참조하던 프리팹·씬이 전부 missing reference가 된다(실측: 재빌드
    //   한 번에 Resident_01.prefab의 m_Graph가 끊겼다). 기존 에셋을 열어 내용만 비우고 다시 채운다 —
    //   BuildRuntimeGraph가 기존 m_RuntimeGraph를 재사용하므로 fileID가 보존된다.
    public static string Build(string path)
    {
        var graph = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(path);

        if (graph == null)
        {
            graph = ScriptableObject.CreateInstance<BehaviorAuthoringGraph>();
            AssetDatabase.CreateAsset(graph, path);
        }
        else
        {
            // 노드는 전부 버리고, 블랙보드는 Self만 남긴다.
            // Self는 패키지가 고정 ID로 관리하는 특수 변수라 지우면 다시 만들어져 링크가 어긋난다.
            graph.Nodes.Clear();
            graph.Roots.Clear();
            graph.Blackboard.Variables.RemoveAll(v => v.Name != "Self");

            // ⚠ 리스트를 비워도 **직렬화된 managed reference는 남는다.** 노드 스크립트를 삭제·개명한 뒤
            //   재빌드하면 그 고아 참조가 "missing type"으로 남아 BuildRuntimeGraph가 통째로 거부한다
            //   ("Graph asset ... has missing types in managed references. Cannot build runtime graph.").
            //   증상은 런타임 그래프가 null이 되는 것뿐이라, 저작 쪽만 보면 정상으로 보인다.
            //
            //   저작 그래프만 청소하면 안 된다 — 실측상 고아는 **컴파일된 BehaviorGraph 서브에셋** 쪽에 남는다.
            //   같은 파일의 모든 오브젝트를 훑는다.
            foreach (Object subAsset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (subAsset != null && SerializationUtility.HasManagedReferencesWithMissingTypes(subAsset))
                {
                    SerializationUtility.ClearAllManagedReferencesWithMissingTypes(subAsset);
                    EditorUtility.SetDirty(subAsset);
                }
            }
        }

        // Self는 신규 그래프에 자동으로 생기는 GameObject 변수다.
        // 노드의 Agent 필드를 여기에 링크하면 컴파일 시 GameObjectToComponentBlackboardVariable이 삽입되어
        // 런타임에 Self.GetComponent<ResidentAgent>()로 해석된다 — 개체마다 값을 꽂아 줄 필요가 없다.
        // (보스 그래프도 같은 방식이다. MidBoss.prefab이 오버라이드하는 변수는 Self 하나뿐이다.)
        VariableModel self = graph.Blackboard.Variables.FirstOrDefault(v => v.Name == "Self");

        if (self == null)
        {
            return "FAIL: 신규 그래프에 Self 변수가 없다.";
        }

        // 목적지는 뽑기 노드의 출력이자 이동 노드의 입력이다. 두 노드를 잇는 값이라 Blackboard로 뺀다.
        var destination = new TypedVariableModel<Vector3> { Name = "Destination" };
        graph.Blackboard.Variables.Add(destination);

        // 목적지 보유 여부. 뽑기 노드가 세우고 이동 노드가 도착 시 내린다 —
        // "목적지가 있으면 계속 그리로, 도착하면 해제, 없으면 새로 받음"이 이 한 값으로 표현된다.
        var hasDestination = new TypedVariableModel<bool> { Name = "HasDestination" };
        graph.Blackboard.Variables.Add(hasDestination);

        // ── 클립 이름은 Blackboard로 올린다 ─────────────────────────────
        //
        // 노드 로컬 값으로 두면 **그래프 에디터가 값을 보여주지 못한다** — 특히 `List<string>`은 노드
        // 인스펙터에서 빈 칸으로 표시되어, 값이 정상으로 실려 있는데도 "연결이 안 됐다"로 읽힌다(실측).
        // Blackboard 변수로 올리면 패널에 목록이 그대로 뜨고 런타임 블랙보드에도 실려 검사가 쉬워진다.
        // 보스 규약의 "수치는 Blackboard 변수로 올린다"(BossNodeReference 「새 노드 추가 절차」 7)와도 맞다.
        var talkStates = new TypedVariableModel<List<string>> { Name = "TalkStates", m_Value = new List<string>(TalkStates) };
        graph.Blackboard.Variables.Add(talkStates);

        var greetState = new TypedVariableModel<string> { Name = "GreetState", m_Value = GreetState };
        graph.Blackboard.Variables.Add(greetState);

        var laughState = new TypedVariableModel<string> { Name = "LaughState", m_Value = LaughState };
        graph.Blackboard.Variables.Add(laughState);

        var surprisedState = new TypedVariableModel<string> { Name = "SurprisedState", m_Value = SurprisedState };
        graph.Blackboard.Variables.Add(surprisedState);

        var danceState = new TypedVariableModel<string> { Name = "DanceState", m_Value = DanceState };
        graph.Blackboard.Variables.Add(danceState);

        var runState = new TypedVariableModel<string> { Name = "RunState", m_Value = RunState };
        graph.Blackboard.Variables.Add(runState);

        // ⚠ 추가 직후 블랙보드를 더티 표시하고 저장해야 한다.
        //   BuildRuntimeGraph는 내부에서 EnsureAssetHasBlackboard()로 블랙보드를 디스크에서 다시 확인하는데,
        //   저장하지 않으면 **방금 추가한 변수가 사라진 채** 컴파일된다.
        //   그러면 GraphAssetProcessor의 GUID→런타임변수 표에 Destination이 없어, 두 노드의 필드가
        //   공유 변수 대신 **각자 로컬 복사본**으로 컴파일된다(실측: 서로 다른 rid 두 개).
        //   증상은 조용하다 — Pick은 자기 복사본에 쓰고 Move는 기본값 (0,0,0)을 읽어
        //   **주민 전원이 월드 원점으로 걸어간다.** 에러도 경고도 없다.
        graph.Blackboard.SetAssetDirty();
        EditorUtility.SetDirty(graph.Blackboard);
        AssetDatabase.SaveAssetIfDirty(graph.Blackboard);

        StartNodeModel start = graph.Nodes.OfType<StartNodeModel>().FirstOrDefault();

        if (start == null)
        {
            start = new StartNodeModel(Unity.Behavior.NodeRegistry.GetInfo(typeof(Start)));
            start.OnDefineNode();
            graph.Nodes.Add(start);
        }

        // 루트가 영원히 반복한다 — Docs/ManagementArea/Resident.md §7의 Repeat (Forever)에 해당한다.
        start.Repeat = true;

        // ── Selector ─────────────────────────────
        //
        // 브랜치를 우선순위로 가른다(§7). 세 브랜치가 서로 배타적이므로 "대화 중에 걸어간다" 같은 조합이
        // 구조로 막힌다 — 노드마다 "지금 대화 중인가"를 심어 막는 것과 다르다.
        var selector = CreateNode(graph, typeof(SelectorComposite), new Vector2(ConversationBranchX, 160f));

        if (selector == null)
        {
            return "FAIL: Selector 생성 실패";
        }

        // ── 브랜치 1: 밤 귀가 (R8 · §3.3) ─────────────────────────────
        //
        // 관찰자가 "밤이다"를 보고 **아래 전부를 끊는다.** 선점 없이 하면 주민 30명이 각자 이동 구간이
        // 끝나기를 기다려 0~4초씩 어긋나게 반응한다 — "해가 지자 마을이 정리된다"가 아니라
        // 뿔뿔이 반응하는 그림이 된다. 이 브랜치가 Priority Abort를 도입한 원래 근거다.
        BehaviorGraphNodeModel goHome = CreateNode(graph, typeof(ResidentGoHomeAction), new Vector2(GoHomeBranchX, 420f));
        BehaviorGraphNodeModel goHomeAbort = CreateNode(graph, typeof(ObserverAbortModifier), new Vector2(GoHomeBranchX, 300f));

        // ── 브랜치 2: 등장 유예 (R9 · §3.2) ─────────────────────────────
        //
        // 관찰자가 없다 — 스포너가 문에서 꺼내는 순간부터 등장 상태이고, 그때 이 주민은 다른 브랜치를
        // 돌고 있지 않다(방금 활성화됐다). 끊을 것이 없으므로 조건만으로 충분하다.
        BehaviorGraphNodeModel exitDoor = CreateNode(graph, typeof(ResidentExitDoorAction), new Vector2(ExitBranchX, 300f));

        // ── 브랜치 3: 대화 (R3 · R4 · R7 · R12) ─────────────────────────────
        BehaviorGraphNodeModel converse = CreateNode(graph, typeof(ResidentConverseAction), new Vector2(ConversationBranchX, 420f));

        // Priority Abort가 대화 브랜치를 감싼다.
        //
        // **여기서 얻는 것**: 말을 걸린 쪽은 아직 산책 브랜치에서 걷고 있다. 관찰자가 "세션 보유"를 매 틱
        // 평가하다가 참이 되면 낮은 우선순위 형제(산책)를 중단시키고 Selector를 처음부터 재평가시킨다 —
        // 그래서 상대가 자기 이동 구간(4초)이 끝나기를 기다리지 않고 그 틱에 합류한다.
        //
        // **이 기계장치는 후속에서 그대로 재사용된다** — 밤 전환(R8)·들려 있음(R10)·이탈 감지가 전부
        // "즉시 끊겨야 하는" 요구다. 그것들을 관찰자 없이 처리하면 긴 노드마다 탈출 조건을 손으로 심어야 하고,
        // 조건과 노드 수의 곱으로 늘어난다.
        BehaviorGraphNodeModel abort = CreateNode(graph, typeof(ObserverAbortModifier), new Vector2(ConversationBranchX, 300f));

        // ── 브랜치 4: 춤이 목격됐을 때의 반응 (R5 후속 「부끄러움」) ─────────────────────────────
        //
        // **관찰자가 이 브랜치의 존재 이유다.** 감시 조건이 참이 되면(춤추는 중에 누가 다가옴) 낮은 우선순위
        // 형제인 춤 브랜치가 중단된다 — 반응 노드가 비어 있어도 **중단은 그것만으로 성립한다.**
        // 반응 클립을 authoring하면 그 자리에서 놀람 → 부끄러움이 붙는다.
        BehaviorGraphNodeModel reaction = CreateNode(graph, typeof(ResidentReactToOnlookerAction), new Vector2(ReactionBranchX, 420f));
        BehaviorGraphNodeModel reactionAbort = CreateNode(graph, typeof(ObserverAbortModifier), new Vector2(ReactionBranchX, 300f));

        // ── 브랜치 5: 춤 (R5 · §10 공연의 선행 형태) ─────────────────────────────
        BehaviorGraphNodeModel dance = CreateNode(graph, typeof(ResidentDanceAction), new Vector2(DanceBranchX, 300f));

        // ── 브랜치 6: 조우 (§7.1 세션 성립) ─────────────────────────────
        BehaviorGraphNodeModel tryStart = CreateNode(graph, typeof(ResidentTryStartConversationAction), new Vector2(EncounterBranchX, 300f));

        // ── 브랜치 7: 산책 (R1 · R2 · R15) ─────────────────────────────
        BehaviorGraphNodeModel pick = CreateNode(graph, typeof(ResidentPickWaypointDestinationAction), new Vector2(StrollBranchX, 360f));
        BehaviorGraphNodeModel move = CreateNode(graph, typeof(ResidentMoveToAction), new Vector2(StrollBranchX, 480f));
        BehaviorGraphNodeModel rest = CreateNode(graph, typeof(ResidentRestAction), new Vector2(StrollBranchX, 600f));

        if (goHome == null || goHomeAbort == null || exitDoor == null ||
            converse == null || abort == null || reaction == null || reactionAbort == null ||
            dance == null || tryStart == null || pick == null || move == null || rest == null)
        {
            return $"FAIL: 노드 생성 실패 goHome={goHome != null} goHomeAbort={goHomeAbort != null} " +
                   $"exitDoor={exitDoor != null} converse={converse != null} abort={abort != null} " +
                   $"reaction={reaction != null} reactionAbort={reactionAbort != null} " +
                   $"dance={dance != null} tryStart={tryStart != null} " +
                   $"pick={pick != null} move={move != null} rest={rest != null}";
        }

        goHome.SetField("Agent", self, typeof(ResidentAgent));
        goHome.SetField("RunState", runState, typeof(string));
        goHome.SetField("CrossFadeSeconds", CrossFadeSeconds);
        goHome.SetField("SpeedFactor", GoHomeSpeedFactor);
        goHome.SetField("ArriveDistance", GoHomeArriveDistance);
        goHome.SetField("MaxSeconds", GoHomeMaxSeconds);

        exitDoor.SetField("Agent", self, typeof(ResidentAgent));
        exitDoor.SetField("Destination", destination, typeof(Vector3));
        exitDoor.SetField("HasDestination", hasDestination, typeof(bool));
        exitDoor.SetField("ExitDistance", ExitDistance);
        exitDoor.SetField("MaxSeconds", ExitMaxSeconds);

        // ⚠ SetField의 세 번째 인자는 **필드의 선언 타입**이지 연결할 변수의 타입이 아니다.
        //   Self는 GameObject인데 여기에 self.Type(GameObject)을 넘기면 GameObject 타입 필드가 만들어지고,
        //   BuildRuntimeGraph의 타입 검증이 그 필드를 선언 타입(ResidentAgent)으로 다시 만들면서
        //   **링크를 조용히 버린다**(실측: linked=Self → linked=null, 경고 없음).
        //   선언 타입을 넘기면 링크가 남고, 컴파일 시 GameObjectToComponentBlackboardVariable이 삽입되어
        //   런타임에 Self.GetComponent<ResidentAgent>()로 해석된다.
        converse.SetField("Agent", self, typeof(ResidentAgent));
        converse.SetField("TalkStates", talkStates, typeof(List<string>));
        converse.SetField("GreetState", greetState, typeof(string));
        converse.SetField("LaughState", laughState, typeof(string));
        converse.SetField("SurprisedState", surprisedState, typeof(string));
        converse.SetField("CrossFadeSeconds", CrossFadeSeconds);
        converse.SetField("FaceToleranceDegrees", FaceToleranceDegrees);
        converse.SetField("StandDistance", ConversationStandDistance);
        converse.SetField("ApproachTimeoutSeconds", ApproachTimeoutSeconds);
        converse.SetField("LaughChance", LaughChance);
        converse.SetField("LaughAfterTurnFraction", LaughAfterTurnFraction);
        converse.SetField("LaughTurnCooldown", LaughTurnCooldown);
        converse.SetField("PendingTimeoutSeconds", ConversationPendingTimeoutSeconds);
        converse.SetField("DisbandCooldownSeconds", ConversationDisbandCooldownSeconds);
        converse.SetField("MaxSeconds", ConversationMaxSeconds);

        reaction.SetField("Agent", self, typeof(ResidentAgent));
        reaction.SetField("ReactionStates", new List<string>(ReactionStates));
        reaction.SetField("CrossFadeSeconds", CrossFadeSeconds);
        reaction.SetField("MaxSeconds", ReactionMaxSeconds);

        dance.SetField("Agent", self, typeof(ResidentAgent));
        dance.SetField("Chance", DanceChance);
        dance.SetField("SoloRadius", DanceSoloRadius);
        dance.SetField("MaxNeighbors", DanceMaxNeighbors);
        dance.SetField("DanceState", danceState, typeof(string));
        dance.SetField("CrossFadeSeconds", CrossFadeSeconds);
        dance.SetField("MinLoops", DanceMinLoops);
        dance.SetField("MaxLoops", DanceMaxLoops);
        dance.SetField("MaxSeconds", DanceMaxSeconds);

        tryStart.SetField("Agent", self, typeof(ResidentAgent));
        tryStart.SetField("Radius", EncounterRadius);
        tryStart.SetField("Chance", EncounterChance);
        tryStart.SetField("FailCooldownSeconds", EncounterFailCooldownSeconds);
        tryStart.SetField("MinTurns", MinTurns);
        tryStart.SetField("MaxTurns", MaxTurns);

        pick.SetField("Agent", self, typeof(ResidentAgent));
        pick.SetField("Destination", destination, typeof(Vector3));
        pick.SetField("HasDestination", hasDestination, typeof(bool));
        pick.SetField("IdleMinSeconds", IdleMinSeconds);
        pick.SetField("IdleMaxSeconds", IdleMaxSeconds);

        move.SetField("Agent", self, typeof(ResidentAgent));
        move.SetField("Destination", destination, typeof(Vector3));
        move.SetField("HasDestination", hasDestination, typeof(bool));
        move.SetField("SegmentSeconds", MoveSegmentSeconds);
        move.SetField("MaxSeconds", MoveTimeoutSeconds);

        rest.SetField("Agent", self, typeof(ResidentAgent));
        rest.SetField("Chance", RestChance);
        rest.SetField("MinRemainingDistance", RestMinRemainingDistance);
        rest.SetField("MinSeconds", RestMinSeconds);
        rest.SetField("MaxSeconds", RestMaxSeconds);

        // 관찰자 3개. 위에서부터 "밤이다"(전부 중단), "세션 보유"(대화 합류), "춤이 목격됨"(춤 중단) 순이다.
        // 전부 LowerPriority — 자기보다 아래 브랜치를 끊는다.
        //
        // 밤 관찰자만 Agent를 안 쓴다 — 페이즈는 전역이라 개체별 참조가 필요 없다.
        if (TryAttachAbortCondition(goHomeAbort, typeof(ResidentIsNightCondition), null,
                out string conditionError) == null)
        {
            return $"FAIL: {conditionError}";
        }

        if (TryAttachAbortCondition(abort, typeof(ResidentHasConversationCondition), self,
                out conditionError) == null)
        {
            return $"FAIL: {conditionError}";
        }

        ConditionModel danceSeen = TryAttachAbortCondition(reactionAbort, typeof(ResidentDanceSeenCondition), self,
            out conditionError);

        if (danceSeen == null)
        {
            return $"FAIL: {conditionError}";
        }

        danceSeen.SetField("Radius", DanceInterruptRadius);
        danceSeen.SetField("MaxNeighbors", DanceMaxNeighbors);

        // 산책 3노드를 한 Sequence로 묶는다. 에디터 UI가 액션을 쌓을 때 만드는 것과 같은 구조라
        // 사람이 그래프를 열었을 때도 손으로 그린 것과 같은 모양으로 보인다.
        var sequence = graph.CreateNode(typeof(SequenceNodeModel), new Vector2(StrollBranchX, 300f)) as SequenceNodeModel;

        if (sequence == null)
        {
            return "FAIL: SequenceNodeModel 생성 실패";
        }

        graph.AddNodeToSequence(pick, sequence, 0);
        graph.AddNodeToSequence(move, sequence, 1);
        graph.AddNodeToSequence(rest, sequence, 2);

        if (!start.TryDefaultOutputPortModel(out PortModel startOut))
        {
            return "FAIL: Start에 기본 출력 포트가 없다.";
        }

        if (!selector.TryDefaultInputPortModel(out PortModel selectorIn))
        {
            return "FAIL: Selector에 기본 입력 포트가 없다.";
        }

        graph.ConnectEdge(startOut, selectorIn);

        if (!selector.TryDefaultOutputPortModel(out PortModel selectorOut))
        {
            return "FAIL: Selector에 기본 출력 포트가 없다.";
        }

        // 자식 7개를 같은 출력 포트에 붙인다(CompositeNodeModel.MaxOutputsAccepted = int.MaxValue).
        // **순서는 여기 순서가 아니라 각 노드의 X 좌표가 정한다** — 위 상수 참조.
        if (!ConnectChild(graph, selectorOut, goHomeAbort) ||
            !ConnectChild(graph, selectorOut, exitDoor) ||
            !ConnectChild(graph, selectorOut, abort) ||
            !ConnectChild(graph, selectorOut, reactionAbort) ||
            !ConnectChild(graph, selectorOut, dance) ||
            !ConnectChild(graph, selectorOut, tryStart) ||
            !ConnectChild(graph, selectorOut, sequence))
        {
            return "FAIL: Selector 자식 연결 실패";
        }

        // 관찰자는 Modifier라 자식이 하나다.
        if (!ConnectModifierChild(graph, goHomeAbort, goHome) ||
            !ConnectModifierChild(graph, abort, converse) ||
            !ConnectModifierChild(graph, reactionAbort, reaction))
        {
            return "FAIL: Priority Abort ↔ 자식 포트 확인 실패";
        }

        // 저작 그래프 → 런타임 그래프 컴파일. 이게 성공해야 BehaviorGraphAgent가 물릴 수 있다.
        BehaviorGraph runtime = graph.BuildRuntimeGraph();

        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssetIfDirty(graph);
        AssetDatabase.ImportAsset(path);

        return Verify(path, graph, runtime);
    }

    // Priority Abort의 감시 조건을 붙이고 그 모델을 돌려준다(추가 필드는 호출부가 채운다).
    //
    // 조건은 노드가 아니라 **관찰자 안에 사는 ConditionModel**이다(에디터에서 노드 위에 조건 줄을 얹는 것과 같다).
    // 필드 링크 규칙은 노드와 같다 — 세 번째 인자에 **선언 타입**을 넘겨야 GameObject→Component 변환이 삽입된다.
    //
    // ⚠ `ObserverAbortTarget.Self`를 쓰면 안 된다. `ObserverAbortModifier.OnStart`가 Self/Both일 때
    //   **조건이 거짓이면 자식을 시작하지 않고 Running을 반환**하는데, 그 상태에서 감시 로직이 다시
    //   중단을 걸어 매 틱 재시작이 반복된다 — Selector가 통째로 멎는다.
    //   "조건이 서면 아래를 끊는다"는 항상 LowerPriority로 표현한다.
    private static ConditionModel TryAttachAbortCondition(BehaviorGraphNodeModel abort, System.Type conditionType,
        VariableModel self, out string error)
    {
        error = null;

        if (abort is not IObserverAbortNodeModel observer)
        {
            error = $"Priority Abort 노드가 IObserverAbortNodeModel이 아니다({abort.GetType().Name}).";
            return null;
        }

        ConditionInfo info = ConditionUtility.GetInfoForConditionType(conditionType);

        if (info == null)
        {
            error = $"{conditionType.Name}의 ConditionInfo를 찾지 못했다. " +
                "[Condition] 특성이 붙어 있고 컴파일이 끝났는지 확인할 것.";
            return null;
        }

        if (System.Activator.CreateInstance(conditionType) is not Condition instance)
        {
            error = $"{conditionType.Name}이 Condition 파생이 아니다.";
            return null;
        }

        var condition = new ConditionModel(abort, instance, info);

        // self가 null이면 개체 참조가 필요 없는 조건이다(예: 밤 판정은 전역 페이즈만 본다).
        // 없는 필드에 링크를 걸면 컴파일 시 "Unhandled variable assignment" 에러가 난다.
        if (self != null)
        {
            condition.SetField("Agent", self, typeof(ResidentAgent));
        }

        observer.ConditionModels.Clear();
        observer.ConditionModels.Add(condition);
        observer.RequiresAllConditionsTrue = true;
        observer.ObserverType = ObserverAbortTarget.LowerPriority;

        return condition;
    }

    private static bool ConnectModifierChild(BehaviorAuthoringGraph graph, BehaviorGraphNodeModel modifier, NodeModel child)
    {
        return modifier.TryDefaultOutputPortModel(out PortModel modifierOut) && ConnectChild(graph, modifierOut, child);
    }

    private static bool ConnectChild(BehaviorAuthoringGraph graph, PortModel parentOut, NodeModel child)
    {
        if (!child.TryDefaultInputPortModel(out PortModel childIn))
        {
            return false;
        }

        graph.ConnectEdge(parentOut, childIn);
        return true;
    }

    // ── 자기검사 ─────────────────────────────
    //
    // 이 그래프의 실패는 거의 전부 조용하다(§11.3). 그래서 빌드 직후에 **디스크와 컴파일된 런타임 그래프를
    // 직접 열어** 확인한다 — 저작 쪽만 보면 전부 정상으로 보이는 경우가 실제로 있었다.
    private static string Verify(string path, BehaviorAuthoringGraph graph, BehaviorGraph runtime)
    {
        var problems = new List<string>();

        // 1) Agent 링크가 컴파일을 넘겼는지. 끊기면 런타임에 노드가 조용히 Failure만 낸다.
        int linkedAgents = graph.Nodes
            .OfType<BehaviorGraphNodeModel>()
            .Count(n => n.Fields.Any(f => f.FieldName == "Agent" && f.LinkedVariable != null));

        // goHome · exitDoor · converse · reaction · dance · tryStart · pick · move · rest 아홉 노드가 Agent를 든다.
        // (관찰자의 조건도 Agent를 들지만 그것은 노드가 아니라 ConditionModel이라 여기 세어지지 않는다 — 3)에서 본다.)
        const int ExpectedAgentLinks = 9;

        if (linkedAgents != ExpectedAgentLinks)
        {
            problems.Add($"Agent 링크가 {ExpectedAgentLinks}개여야 하는데 {linkedAgents}개다 " +
                "(goHome·exitDoor·converse·reaction·dance·tryStart·pick·move·rest 중 누락).");
        }

        // 2) 공유 변수가 런타임 블랙보드에 실렸는지.
        //    저작 링크가 살아 있어도 여기 없으면 각 노드가 로컬 복사본으로 컴파일되어 값을 주고받지 못한다.
        var runtimeBlackboard = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<RuntimeBlackboardAsset>()
            .FirstOrDefault();

        string runtimeVars = runtimeBlackboard != null
            ? string.Join(",", runtimeBlackboard.Blackboard.Variables.Select(v => v.Name))
            : "블랙보드 없음";

        var expectedVars = new[]
        {
            "Destination", "HasDestination", "TalkStates",
            "GreetState", "LaughState", "SurprisedState", "DanceState", "RunState",
        };

        List<string> missingVars = runtimeBlackboard == null
            ? expectedVars.ToList()
            : expectedVars.Where(n => !runtimeBlackboard.Blackboard.Variables.Any(v => v.Name == n)).ToList();

        if (missingVars.Count > 0)
        {
            problems.Add($"런타임 블랙보드에 없는 변수: [{string.Join(",", missingVars)}]. " +
                "노드가 각자 로컬 복사본을 갖게 되어 값을 주고받지 못한다 " +
                "(Destination이 빠지면 주민 전원이 월드 원점으로 걸어간다).");
        }

        // 3) 선점이 살아남았는지. 컴파일된 런타임 그래프를 직접 걷는다.
        //    조건 링크가 끊기거나 관찰자 등록이 빠지면 **경고 하나 없이 선점만 사라진다** —
        //    합류가 최대 4초 늦어지는 것으로만 드러나 원인을 찾기 어렵다.
        string abortState = VerifyAbort(runtime, problems,
            out ResidentConverseAction converseNode, out ResidentDanceAction danceNode);

        // 4) 클립 이름이 실제로 실렸고 컨트롤러에 그 상태가 있는지. 비거나 틀리면 노드가 상한 뒤에
        //    경고만 남기고 **아무 클립도 재생하지 않는다** — 두 주민이 마주 서 있기만 한다.
        string converseState = VerifyClipStates(converseNode, danceNode, problems);

        bool ok = problems.Count == 0;

        if (!ok)
        {
            foreach (string problem in problems)
            {
                Debug.LogError($"[ResidentBehaviorGraphBuilder] {problem}");
            }
        }

        return $"{(ok ? "OK" : "FAIL")} path={path} nodes={graph.Nodes.Count} " +
               $"linkedAgents={linkedAgents} runtimeVars=[{runtimeVars}] abort={abortState} " +
               $"converse={converseState} " +
               $"runtime={runtime != null} rootGraph={(runtime != null && runtime.RootGraph != null)}";
    }

    // 대화 노드에 클립 이름이 실렸는지 확인한다.
    //
    // 이 필드들은 Blackboard 변수가 아니라 **로컬 값**으로 실린다. 로컬 값이 유실되면 노드는 실패하지 않고
    // 상한(1.5초)마다 경고만 남기며 아무 클립도 재생하지 않는다 — 두 주민이 마주 서 있기만 하는 그림이라
    // "대화는 도는데 애니메이션만 없다"로 보여 원인이 애니메이터 쪽으로 오해된다.
    private static string VerifyClipStates(ResidentConverseAction converse, ResidentDanceAction dance, List<string> problems)
    {
        if (converse == null)
        {
            problems.Add("컴파일된 그래프에서 Resident Converse 노드를 찾지 못했다.");
            return "노드 없음";
        }

        List<string> talkStates = converse.TalkStates != null ? converse.TalkStates.Value : null;
        int talkCount = talkStates != null ? talkStates.Count : -1;

        if (talkCount != TalkStates.Count)
        {
            problems.Add($"TalkStates가 {TalkStates.Count}개여야 하는데 {talkCount}개다" +
                (talkCount < 0 ? "(변수 자체가 null)" : string.Empty) +
                ". 비면 수다 클립이 하나도 재생되지 않는다.");
        }

        string greet = converse.GreetState != null ? converse.GreetState.Value : null;
        string laugh = converse.LaughState != null ? converse.LaughState.Value : null;
        string surprised = converse.SurprisedState != null ? converse.SurprisedState.Value : null;
        string danceStateName = dance != null && dance.DanceState != null ? dance.DanceState.Value : null;

        if (dance == null)
        {
            problems.Add("컴파일된 그래프에서 Resident Dance 노드를 찾지 못했다.");
        }

        if (string.IsNullOrEmpty(greet) || string.IsNullOrEmpty(laugh) ||
            string.IsNullOrEmpty(surprised) || string.IsNullOrEmpty(danceStateName))
        {
            problems.Add($"클립 상태 이름이 비었다 (greet='{greet}' laugh='{laugh}' " +
                $"surprised='{surprised}' dance='{danceStateName}').");
        }

        // 이름이 실렸어도 컨트롤러에 같은 이름의 상태가 없으면 결과가 같다(재생 안 됨).
        // 상태 이름은 문자열이라 컴파일이 잡아 주지 않으므로 여기서 대조한다.
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);

        if (controller == null)
        {
            problems.Add($"AnimatorController를 찾지 못했다: {ControllerPath}");
            return $"talk={talkCount} 컨트롤러 없음";
        }

        var stateNames = new HashSet<string>();

        foreach (UnityEditor.Animations.ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            stateNames.Add(child.state.name);
        }

        var required = new List<string> { greet, laugh, surprised, danceStateName, RunState };

        if (talkStates != null)
        {
            required.AddRange(talkStates);
        }

        List<string> missing = required
            .Where(s => !string.IsNullOrEmpty(s) && !stateNames.Contains(s))
            .Distinct()
            .ToList();

        if (missing.Count > 0)
        {
            problems.Add($"컨트롤러에 없는 상태 이름: [{string.Join(",", missing)}]. " +
                "노드가 그 상태에 도달하지 못해 재생 없이 넘어간다.");
        }

        return $"talk={talkCount} greet={greet} laugh={laugh} surprised={surprised} " +
               $"dance={danceStateName} missingStates={missing.Count}";
    }

    private static string VerifyAbort(BehaviorGraph runtime, List<string> problems,
        out ResidentConverseAction converse, out ResidentDanceAction dance)
    {
        converse = null;
        dance = null;

        Node root = runtime != null && runtime.RootGraph != null ? runtime.RootGraph.Root : null;

        // 루트는 Start(Modifier)이고 그 자식이 Selector다.
        var selector = root is Modifier startModifier ? startModifier.Child as SelectorComposite : null;

        if (selector == null)
        {
            problems.Add("컴파일된 그래프의 루트 아래에서 Selector를 찾지 못했다.");
            return "selector 없음";
        }

        const int ExpectedChildren = 7;

        if (selector.Children.Count != ExpectedChildren)
        {
            problems.Add($"Selector 자식이 {ExpectedChildren}개여야 하는데 {selector.Children.Count}개다.");
        }

        // 브랜치 우선순위는 X 좌표가 정한다. 순서가 어긋나면 조건도 등록도 정상인 채로 **선점만 조용히 죽는다** —
        // LowerPriority는 자기보다 아래 형제만 끊기 때문이다. 그래서 순서를 타입으로 직접 대조한다.
        //  [0] 밤 귀가 관찰자   — 아래 전부를 끊는다
        //  [1] 등장 유예        — 관찰자 없음(끊을 대상이 없다)
        //  [2] 대화 관찰자      — 산책을 끊고 합류시킨다
        //  [3] 목격 반응 관찰자 — 춤을 끊는다
        //  [4] 춤               — 조우보다 위
        var goHomeObserver = ChildAt(selector, 0) as ObserverAbortModifier;

        if (goHomeObserver == null || goHomeObserver.Child is not ResidentGoHomeAction)
        {
            problems.Add("Selector의 첫 자식이 밤 귀가 관찰자(Priority Abort → Go Home)가 아니다. " +
                "이 브랜치가 아래로 내려가면 **밤이 와도 대화·춤이 끊기지 않는다.**");
        }

        if (ChildAt(selector, 1) is not ResidentExitDoorAction)
        {
            problems.Add("Selector의 두 번째 자식이 등장 유예(Exit Door)가 아니다. " +
                "아래로 내려가면 문에서 나오자마자 다른 브랜치가 평가된다.");
        }

        var reactionObserver = ChildAt(selector, 3) as ObserverAbortModifier;

        if (reactionObserver == null || reactionObserver.Child is not ResidentReactToOnlookerAction)
        {
            problems.Add("Selector의 네 번째 자식이 반응 관찰자(Priority Abort → React To Onlooker)가 아니다. " +
                "이 브랜치가 춤보다 아래로 내려가면 **춤이 중단되지 않는다.**");
        }

        dance = ChildAt(selector, 4) as ResidentDanceAction;

        if (dance == null)
        {
            Node actualNode = ChildAt(selector, 4);
            string actual = actualNode != null ? actualNode.GetType().Name : "없음";

            problems.Add($"Selector의 다섯 번째 자식이 춤이 아니라 {actual}이다. " +
                "브랜치 X 좌표 상수를 확인할 것 (자식 순서는 Position.x 오름차순으로 결정된다).");
        }

        // 대화 관찰자. 이 아래의 조건·바인딩 검사는 이 관찰자를 기준으로 한다.
        var observer = ChildAt(selector, 2) as ObserverAbortModifier;

        if (observer == null)
        {
            problems.Add("Selector의 세 번째 자식이 대화 관찰자(Priority Abort)가 아니다. " +
                "브랜치 X 좌표 상수를 확인할 것 (자식 순서는 Position.x 오름차순으로 결정된다).");
            return "대화 관찰자 아님";
        }

        converse = observer.Child as ResidentConverseAction;

        int conditions = observer.Conditions != null ? observer.Conditions.Count : 0;

        if (conditions != 1)
        {
            problems.Add($"Priority Abort의 조건이 1개여야 하는데 {conditions}개다.");
        }

        // 조건의 Agent가 비면 IsTrue가 항상 거짓이 되어 선점이 영구히 잠긴다.
        bool agentBound = observer.Conditions != null &&
            observer.Conditions.OfType<ResidentHasConversationCondition>().Any(c => c.Agent != null);

        if (!agentBound)
        {
            problems.Add("Priority Abort 조건의 Agent가 비어 있다. 선점이 영구히 발동하지 않는다.");
        }

        if (observer.AbortTarget != ObserverAbortTarget.LowerPriority)
        {
            problems.Add($"AbortTarget이 LowerPriority가 아니다({observer.AbortTarget}).");
        }

        // 관찰자가 부모 Selector에 등록됐는지. 이게 빠지면 조건이 아무리 참이어도 아무 일도 일어나지 않는다.
        int registered = selector.m_RegisteredObservers != null ? selector.m_RegisteredObservers.Count : 0;

        if (registered != 3)
        {
            problems.Add($"Selector에 등록된 관찰자가 3개여야 하는데 {registered}개다 " +
                "(밤 귀가 · 대화 합류 · 춤 중단).");
        }

        // 밤 관찰자의 조건. Agent를 안 쓰는 유일한 조건이라 바인딩 대신 존재만 확인한다.
        bool nightBound = goHomeObserver != null && goHomeObserver.Conditions != null &&
            goHomeObserver.Conditions.OfType<ResidentIsNightCondition>().Any();

        if (!nightBound)
        {
            problems.Add("밤 귀가 관찰자의 조건(ResidentIsNightCondition)이 없다. " +
                "밤이 와도 대화·춤이 끊기지 않는다.");
        }

        // 춤 중단 관찰자의 조건도 Agent가 물려 있어야 한다. 비면 IsTrue가 항상 거짓이라 **춤이 영원히 안 끊긴다.**
        bool danceSeenBound = reactionObserver != null && reactionObserver.Conditions != null &&
            reactionObserver.Conditions.OfType<ResidentDanceSeenCondition>().Any(c => c.Agent != null);

        if (!danceSeenBound)
        {
            problems.Add("춤 중단 관찰자의 조건(ResidentDanceSeenCondition)이 없거나 Agent가 비어 있다. " +
                "춤이 목격돼도 중단되지 않는다.");
        }

        return $"conditions={conditions} agentBound={agentBound} target={observer.AbortTarget} " +
               $"nightBound={nightBound} danceSeenBound={danceSeenBound} registered={registered}";
    }

    private static Node ChildAt(Composite composite, int index)
    {
        return composite != null && index >= 0 && index < composite.Children.Count ? composite.Children[index] : null;
    }

    private static BehaviorGraphNodeModel CreateNode(BehaviorAuthoringGraph graph, System.Type nodeType, Vector2 position)
    {
        NodeInfo info = Unity.Behavior.NodeRegistry.GetInfo(nodeType);

        if (info == null)
        {
            Debug.LogError($"[ResidentBehaviorGraphBuilder] {nodeType.Name}의 NodeInfo를 찾지 못했다. " +
                "[NodeDescription] 특성이 붙어 있고 컴파일이 끝났는지 확인할 것.");
            return null;
        }

        return graph.CreateNode(info.ModelType, position, null, new object[] { info }) as BehaviorGraphNodeModel;
    }
}
