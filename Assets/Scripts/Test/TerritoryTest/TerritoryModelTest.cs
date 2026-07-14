using UnityEngine;

/// <summary>
/// <see cref="TerritoryGraph"/> 도메인 모델의 Play 검증 하네스 (씬: TerritoryTest.unity).<br/>
/// 유닛 테스트가 없는 프로젝트 관례대로 5노드 다이아몬드 그래프를 수동 구성해
/// 상태 전이·프론티어 승격·이벤트 발화 횟수를 기대값과 대조하고 [TerritoryTest] PASS/FAIL 로그를 남긴다.<br/>
/// 실패가 하나라도 있으면 LogError를 발생시켜 콘솔 에러 검증에 걸리게 한다.
/// <code>
///      1
///     / \
///    0   3 ─ 4      (본진 0, 엣지: 0-1, 0-2, 1-3, 2-3, 3-4)
///     \ /
///      2
/// </code>
/// </summary>
public class TerritoryModelTest : MonoBehaviour
{
    private int _passCount;
    private int _failCount;

    private void Start()
    {
        var n0 = new TerritoryNode(0, Vector3.zero);
        var n1 = new TerritoryNode(1, new Vector3(1f, 0f, 1f));
        var n2 = new TerritoryNode(2, new Vector3(1f, 0f, -1f));
        var n3 = new TerritoryNode(3, new Vector3(2f, 0f, 0f));
        var n4 = new TerritoryNode(4, new Vector3(3f, 0f, 0f));
        TerritoryNode.Connect(n0, n1);
        TerritoryNode.Connect(n0, n2);
        TerritoryNode.Connect(n1, n3);
        TerritoryNode.Connect(n2, n3);
        TerritoryNode.Connect(n3, n4);

        var graph = new TerritoryGraph(new[] { n0, n1, n2, n3, n4 }, homeNodeId: 0);

        int changedCount = 0;
        int claimedCount = 0;
        TerritoryNode lastClaimed = null;
        graph.OnChanged += () => changedCount++;
        graph.OnNodeClaimed += node =>
        {
            claimedCount++;
            lastClaimed = node;
        };

        // 1) 초기화 (§4.3-1): 본진 Owned, 인접 Selectable, 나머지 Locked
        Check(n0.State == TerritoryState.Owned, "초기화: 본진(0)은 Owned");
        Check(n1.State == TerritoryState.Selectable && n2.State == TerritoryState.Selectable,
            "초기화: 본진 인접(1,2)은 Selectable");
        Check(n3.State == TerritoryState.Locked && n4.State == TerritoryState.Locked,
            "초기화: 나머지(3,4)는 Locked");
        Check(graph.OwnedCount == 1, "초기화: OwnedCount == 1");
        Check(graph.IsRevealed(1) && !graph.IsRevealed(3), "초기화: 공개 여부(1 공개, 3 숨김)");
        Check(CountFrontier(graph) == 2, "초기화: 프론티어 2개");

        // 2) 거부 (§4.3-3): Locked/Owned는 확보 불가, 거부 시 이벤트 미발화
        Check(!graph.TryClaim(3), "거부: Locked(3) 확보 불가");
        Check(!graph.TryClaim(0), "거부: Owned(0) 확보 불가");
        Check(changedCount == 0 && claimedCount == 0, "거부: 이벤트 미발화");

        // 3) 확보 (§4.3-2): 1 확보 → 이웃(3)만 승격, 비인접(4)은 유지
        Check(graph.TryClaim(1), "확보: Selectable(1) 확보 성공");
        Check(n1.State == TerritoryState.Owned, "확보: 1 → Owned 전이");
        Check(n3.State == TerritoryState.Selectable, "확보: 이웃 3 → Selectable 승격");
        Check(n4.State == TerritoryState.Locked, "확보: 비인접 4는 Locked 유지");
        Check(claimedCount == 1 && lastClaimed == n1, "확보: OnNodeClaimed 1회(노드 1)");
        Check(changedCount == 1, "확보: OnChanged 1회");

        // 4) 연쇄 확장: 프론티어가 번져나가고, 전부 확보하면 소진된다
        Check(graph.TryClaim(3) && n4.State == TerritoryState.Selectable, "연쇄: 3 확보 → 4 승격");
        Check(graph.TryClaim(2) && graph.TryClaim(4), "연쇄: 2, 4 확보");
        Check(graph.OwnedCount == 5 && CountFrontier(graph) == 0, "연쇄: 전부 보유·프론티어 소진");
        Check(claimedCount == 4 && changedCount == 4, "연쇄: 이벤트 총 4회(확보당 1회)");

        if (_failCount == 0)
        {
            Debug.Log($"[TerritoryTest] 모델 검증 PASS ({_passCount}/{_passCount + _failCount})");
        }
        else
        {
            Debug.LogError($"[TerritoryTest] 모델 검증 FAIL ({_failCount}건 실패 / 총 {_passCount + _failCount})");
        }
    }

    private void Check(bool condition, string label)
    {
        if (condition)
        {
            _passCount++;
        }
        else
        {
            _failCount++;
            Debug.LogError($"[TerritoryTest] FAIL: {label}");
        }
    }

    private int CountFrontier(TerritoryGraph graph)
    {
        int count = 0;
        foreach (TerritoryNode _ in graph.Frontier)
        {
            count++;
        }
        return count;
    }
}
