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
//
// ⚠ NodeRegistry는 Unity.Behavior와 Unity.Behavior.GraphFramework 양쪽에 있어 그냥 쓰면 CS0104가 난다.
//   반드시 정규화할 것.
public static class ResidentBehaviorGraphBuilder
{
    public const string GraphPath = "Assets/Behavior/ResidentBehavior.asset";

    // R1 유휴 — 도착해서 다음 목적지를 정하기 전까지 머무는 시간.
    // 고정값이면 도착 시각이 비슷한 주민들이 같은 박자로 동시에 출발하므로 구간에서 뽑는다.
    private const float IdleMinSeconds = 2f;

    private const float IdleMaxSeconds = 5f;

    // 이동 상한(초). 도달 불가능한 지점이 잡혔을 때 브랜치가 멎는 것을 막는 안전장치다.
    // 목적지가 웨이포인트가 되면서 이동 거리가 맵 크기에 비례하므로, 예전(제자리 근처 산책)보다 넉넉해야 한다.
    private const float MoveTimeoutSeconds = 60f;

    // 한 번에 걷는 구간(초). 도착 전이라도 여기서 브랜치를 끊어 휴식 판정 자리를 만든다.
    // 짧으면 판정이 잦아 확률을 낮춰야 하고, 길면 짧은 여정에서 한 번도 안 굴려진다.
    private const float MoveSegmentSeconds = 4f;

    // ── R15 휴식 ─────────────────────────────
    // 구간마다 한 번 판정하므로 **여정이 길수록 판정이 잦다** — 먼 웨이포인트일수록 자연히 여러 번 쉰다.
    // 50유닛(속도 1.5 → 약 33초) 여정이면 약 7회 판정 → 평균 1.7회 휴식.
    // 10유닛(약 7초)이면 1회 판정 → 25% 확률로 한 번.

    // 0.25로 시작했다가 실제로 걷는 것을 보고 0.15로 낮췄다 — 너무 자주 서면 산책이 아니라
    // 자꾸 멈칫하는 그림이 된다.
    private const float RestChance = 0.15f;

    // 남은 거리가 이보다 짧으면 쉬지 않는다. 속도 1.5 기준 약 4초 거리 — "곧 도착"의 경계다.
    private const float RestMinRemainingDistance = 6f;

    // 휴식 길이. 고정값이면 박자가 맞아떨어지므로 구간에서 뽑는다.
    private const float RestMinSeconds = 1.5f;

    private const float RestMaxSeconds = 3.5f;

    [MenuItem("NorthLand/Resident/Rebuild Behavior Graph")]
    private static void RebuildFromMenu()
    {
        Debug.Log(Build(GraphPath));
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

        BehaviorGraphNodeModel pick = CreateNode(graph, typeof(ResidentPickWaypointDestinationAction), new Vector2(0f, 300f));
        BehaviorGraphNodeModel move = CreateNode(graph, typeof(ResidentMoveToAction), new Vector2(0f, 420f));
        BehaviorGraphNodeModel rest = CreateNode(graph, typeof(ResidentRestAction), new Vector2(0f, 540f));

        if (pick == null || move == null || rest == null)
        {
            return $"FAIL: 노드 생성 실패 pick={pick != null} move={move != null} rest={rest != null}";
        }

        // ⚠ SetField의 세 번째 인자는 **필드의 선언 타입**이지 연결할 변수의 타입이 아니다.
        //   Self는 GameObject인데 여기에 self.Type(GameObject)을 넘기면 GameObject 타입 필드가 만들어지고,
        //   BuildRuntimeGraph의 타입 검증이 그 필드를 선언 타입(ResidentAgent)으로 다시 만들면서
        //   **링크를 조용히 버린다**(실측: linked=Self → linked=null, 경고 없음).
        //   선언 타입을 넘기면 링크가 남고, 컴파일 시 GameObjectToComponentBlackboardVariable이 삽입되어
        //   런타임에 Self.GetComponent<ResidentAgent>()로 해석된다.
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

        // 세 노드를 한 Sequence로 묶는다. 에디터 UI가 액션을 쌓을 때 만드는 것과 같은 구조라
        // 사람이 그래프를 열었을 때도 손으로 그린 것과 같은 모양으로 보인다.
        var sequence = graph.CreateNode(typeof(SequenceNodeModel), new Vector2(0f, 240f)) as SequenceNodeModel;

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

        if (!sequence.TryDefaultInputPortModel(out PortModel sequenceIn))
        {
            return "FAIL: Sequence에 기본 입력 포트가 없다.";
        }

        graph.ConnectEdge(startOut, sequenceIn);

        // 저작 그래프 → 런타임 그래프 컴파일. 이게 성공해야 BehaviorGraphAgent가 물릴 수 있다.
        BehaviorGraph runtime = graph.BuildRuntimeGraph();

        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssetIfDirty(graph);
        AssetDatabase.ImportAsset(path);

        // Agent 링크가 컴파일을 넘겼는지 확인한다. 여기가 끊기면 런타임에 노드가 조용히 Failure만 낸다.
        int linkedAgents = graph.Nodes
            .OfType<BehaviorGraphNodeModel>()
            .Count(n => n.Fields.Any(f => f.FieldName == "Agent" && f.LinkedVariable != null));

        // ⚠ 저작 링크가 살아 있어도 **런타임 블랙보드에 변수가 실리지 않으면** 각 노드가 로컬 복사본으로
        //   컴파일되어, 노드끼리 값을 주고받지 못한다. 저작 쪽만 보면 정상으로 보이므로 디스크로 확인한다.
        var runtimeBlackboard = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<RuntimeBlackboardAsset>()
            .FirstOrDefault();

        string runtimeVars = runtimeBlackboard != null
            ? string.Join(",", runtimeBlackboard.Blackboard.Variables.Select(v => v.Name))
            : "블랙보드 없음";

        bool shared = runtimeBlackboard != null &&
            runtimeBlackboard.Blackboard.Variables.Any(v => v.Name == "Destination") &&
            runtimeBlackboard.Blackboard.Variables.Any(v => v.Name == "HasDestination");

        if (!shared)
        {
            Debug.LogError("[ResidentBehaviorGraphBuilder] Destination / HasDestination이 런타임 블랙보드에 없다. " +
                "두 노드가 각자 로컬 복사본을 갖게 되어 값을 주고받지 못하고, 주민 전원이 월드 원점으로 걸어간다.");
        }

        return $"{(shared ? "OK" : "FAIL")} path={path} nodes={graph.Nodes.Count} " +
               $"linkedAgents={linkedAgents} runtimeVars=[{runtimeVars}] " +
               $"runtime={runtime != null} rootGraph={(runtime != null && runtime.RootGraph != null)}";
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
