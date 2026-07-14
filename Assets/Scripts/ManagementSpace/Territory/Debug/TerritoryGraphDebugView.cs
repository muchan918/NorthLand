using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 생성기 검증용 샌드박스 컴포넌트 (씬: TerritoryGraphSandbox.unity) — 게임 로직과 무관한 개발 도구.<br/>
/// 인스펙터 값이 바뀔 때마다 파이프라인 전체(산포→Delaunay→트리→프루닝)를 재생성해
/// <b>씬 뷰 Gizmo</b>로 단계별 산출물을 그리고(플레이 불필요),
/// 컴포넌트 우클릭 컨텍스트 메뉴로 결정성·불변식을 코드로도 검증한다.<br/>
/// 검증 실패는 LogError로 남겨 콘솔 에러 검사에 걸리게 한다.
/// </summary>
public class TerritoryGraphDebugView : MonoBehaviour
{
    // 파이프라인 단계별 산출물 비교용
    private enum Stage
    {
        Scatter,   // 점만
        Delaunay,  // 삼각망 전체
        Tree,      // 스패닝 트리만
        Pruned,    // 최종(트리 + 유지된 추가 엣지). 버려진 엣지는 희미하게 표시
    }

    [SerializeField] TerritoryGraphGenSettings _settings = new();

    [Tooltip("생성 시드. 같은 (설정, 시드)는 항상 같은 결과 — 바꾸면 새 지형")]
    [SerializeField] int _seed = 12345;

    [Tooltip("어느 단계까지의 산출물을 그릴지")]
    [SerializeField] Stage _drawStage = Stage.Pruned;

    private List<Vector2> _positions;
    private List<TerritoryEdge> _delaunayEdges;
    private List<TerritoryEdge> _treeEdges;
    private List<TerritoryEdge> _prunedEdges;

    [ContextMenu("재생성")]
    public void Regenerate()
    {
        GeneratePipeline();

        int extraCount = _prunedEdges.Count - _treeEdges.Count;
        Debug.Log($"[영토검증] 생성(seed={_seed}): 노드 {_positions.Count}, " +
                  $"Delaunay {_delaunayEdges.Count} → 최종 {_prunedEdges.Count} (트리 {_treeEdges.Count} + 추가 {extraCount}), " +
                  $"평균 차수 {AverageDegree():F2}, 최대 차수 {MaxDegree()}, 본진 차수 {HomeDegree()}, " +
                  $"최소 쌍거리 {MinPairDistance():F2}");
    }

    /// <summary>같은 시드 2회 생성 → 위치·최종 엣지 완전 일치해야 한다(WL-008 시드 재현성).</summary>
    [ContextMenu("결정성 검증")]
    public void VerifyDeterminism()
    {
        TerritoryGraphLayout a = TerritoryGraphGenerator.Generate(_settings, _seed);
        TerritoryGraphLayout b = TerritoryGraphGenerator.Generate(_settings, _seed);

        bool same = a.Positions.Count == b.Positions.Count && a.Edges.Count == b.Edges.Count;
        for (int i = 0; same && i < a.Positions.Count; i++)
        {
            // Vector2 ==는 근사 비교(오차 허용)라 쓰지 않는다 — 결정성은 비트 단위 동일이어야 한다.
            same = a.Positions[i].x == b.Positions[i].x && a.Positions[i].y == b.Positions[i].y;
        }
        for (int i = 0; same && i < a.Edges.Count; i++)
        {
            same = a.Edges[i].Equals(b.Edges[i]);
        }

        if (same)
        {
            Debug.Log($"[영토검증] 결정성 PASS — 같은 시드({_seed}) 2회 생성 완전 일치 " +
                      $"(노드 {a.Positions.Count}, 최종 엣지 {a.Edges.Count})");
        }
        else
        {
            Debug.LogError($"[영토검증] 결정성 FAIL — 같은 시드({_seed})인데 결과가 다릅니다!");
        }
    }

    /// <summary>
    /// 불변식: 본진=원점 / 반지름·부채꼴 내 / 최소 간격 + 트리 크기(n-1) +
    /// 최종 그래프의 평면성(교차 0)·연결성(본진 전 노드 도달) + 본진 선택지(차수 ≥ 2).
    /// </summary>
    [ContextMenu("불변식 검증")]
    public void VerifyInvariants()
    {
        if (_positions == null || _positions.Count == 0)
        {
            Regenerate();
        }

        int fail = 0;

        // ── 산포 불변식 ──
        if (_positions[0] != Vector2.zero)
        {
            Debug.LogError($"[영토검증] FAIL: 본진(0)이 원점이 아닙니다: {_positions[0]}");
            fail++;
        }

        for (int i = 0; i < _positions.Count; i++)
        {
            if (_positions[i].magnitude > _settings.AreaRadius + 0.001f)
            {
                Debug.LogError($"[영토검증] FAIL: {i}번 노드가 산포 원 밖입니다: {_positions[i]}");
                fail++;
            }
        }

        float minDist = MinPairDistance();
        if (minDist < _settings.MinNodeSpacing - 0.001f)
        {
            Debug.LogWarning($"[영토검증] 최소 쌍거리 {minDist:F2} < 설정 간격 {_settings.MinNodeSpacing:F2} " +
                             "— 간격 완화가 발생한 설정인지 확인하세요.");
        }

        // ── 부채꼴: 모든 노드가 확장 방향 각도 범위 안 (본진 제외 — 원점은 각도 미정의) ──
        if (_settings.SectorAngleDegrees < 360f)
        {
            float halfAngle = _settings.SectorAngleDegrees * 0.5f + 0.1f;
            for (int i = 1; i < _positions.Count; i++)
            {
                float angle = Mathf.Atan2(_positions[i].y, _positions[i].x) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(_settings.SectorCenterDegrees, angle)) > halfAngle)
                {
                    Debug.LogError($"[영토검증] FAIL: {i}번 노드가 확장 부채꼴 밖입니다 (각도 {angle:F1}°)");
                    fail++;
                }
            }
        }

        // ── 트리: 정확히 n-1개 엣지 ──
        if (_treeEdges.Count != _positions.Count - 1)
        {
            Debug.LogError($"[영토검증] FAIL: 트리 엣지 수 {_treeEdges.Count} != n-1 ({_positions.Count - 1})");
            fail++;
        }

        // ── 최종 그래프 평면성: 어떤 엣지 쌍도 내부에서 교차하지 않는다 (§4.1 평면 그래프) ──
        for (int i = 0; i < _prunedEdges.Count; i++)
        {
            for (int j = i + 1; j < _prunedEdges.Count; j++)
            {
                TerritoryEdge e1 = _prunedEdges[i];
                TerritoryEdge e2 = _prunedEdges[j];
                if (e1.A == e2.A || e1.A == e2.B || e1.B == e2.A || e1.B == e2.B)
                {
                    continue;
                }
                if (TerritoryGeometry.SegmentsProperlyIntersect(
                        _positions[e1.A], _positions[e1.B], _positions[e2.A], _positions[e2.B]))
                {
                    Debug.LogError($"[영토검증] FAIL: 엣지 교차 {e1} × {e2}");
                    fail++;
                }
            }
        }

        // ── 최종 그래프 연결성: 트리를 보존했으므로 프루닝 후에도 반드시 성립해야 한다 ──
        if (!TerritoryGraphGenerator.IsConnected(_prunedEdges, _positions.Count))
        {
            Debug.LogError("[영토검증] FAIL: 최종 그래프에 본진에서 도달 불가한 노드가 있습니다.");
            fail++;
        }

        // ── 본진 선택지: 첫 프론티어 최소 2개 (GDD §6.3 "하나 선택"의 전제, n≥3일 때) ──
        if (_positions.Count >= 3 && HomeDegree() < 2)
        {
            Debug.LogWarning($"[영토검증] 본진 차수 {HomeDegree()} < 2 — 첫 확장 선택지가 하나뿐입니다.");
        }

        if (fail == 0)
        {
            Debug.Log($"[영토검증] 불변식 PASS (노드 {_positions.Count}, 최종 엣지 {_prunedEdges.Count}, " +
                      $"트리 {_treeEdges.Count}, 교차 0, 연결성 OK, 최소 쌍거리 {minDist:F2})");
        }
    }

    // 파이프라인 1회 실행 — rng 소비 순서(산포 → 트리 → 추가 엣지)를 Generate와 동일하게 유지해야
    // 단계별 산출물이 Generate(settings, seed)의 실제 결과와 일치한다.
    private void GeneratePipeline()
    {
        var rng = new System.Random(_seed);
        _positions = TerritoryGraphGenerator.ScatterPositions(_settings, rng);
        _delaunayEdges = TerritoryGraphGenerator.Triangulate(_positions);
        _treeEdges = TerritoryGraphGenerator.BuildSpanningTree(
            _delaunayEdges, _positions, _settings.TreeShortEdgeBias, rng);
        _prunedEdges = TerritoryGraphGenerator.Prune(_delaunayEdges, _treeEdges, _positions,
            _settings.ExtraEdgeKeepRatio, _settings.ExtraEdgeMaxLengthRatio, rng);
    }

    private float AverageDegree() =>
        _positions.Count > 0 ? 2f * _prunedEdges.Count / _positions.Count : 0f;

    private int MaxDegree()
    {
        var degrees = new int[_positions.Count];
        for (int i = 0; i < _prunedEdges.Count; i++)
        {
            degrees[_prunedEdges[i].A]++;
            degrees[_prunedEdges[i].B]++;
        }

        int max = 0;
        for (int i = 0; i < degrees.Length; i++)
        {
            max = Mathf.Max(max, degrees[i]);
        }
        return max;
    }

    // 정규화 규약상 본진(0)이 낀 엣지는 항상 A == 0.
    private int HomeDegree()
    {
        int count = 0;
        for (int i = 0; i < _prunedEdges.Count; i++)
        {
            if (_prunedEdges[i].A == 0)
            {
                count++;
            }
        }
        return count;
    }

    private float MinPairDistance()
    {
        if (_positions == null || _positions.Count < 2)
        {
            return float.PositiveInfinity;
        }

        float min = float.PositiveInfinity;
        for (int i = 0; i < _positions.Count; i++)
        {
            for (int j = i + 1; j < _positions.Count; j++)
            {
                min = Mathf.Min(min, (_positions[i] - _positions[j]).magnitude);
            }
        }
        return min;
    }

    // 인스펙터 편집 즉시 반영 (에디터 전용 콜백)
    private void OnValidate()
    {
        GeneratePipeline();
    }

    // 생성기는 2D(Vector2)로 일하고, 뷰가 XZ 평면에 얹는다 — 모델 조립 때도 동일 규약.
    private Vector3 ToWorld(Vector2 p) => transform.position + new Vector3(p.x, 0f, p.y);

    private void OnDrawGizmos()
    {
        if (_positions == null || _positions.Count == 0)
        {
            return;
        }

        // 산포 영역(부채꼴 — 본진 꼭짓점에서 확장 방향으로)
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        DrawSectorXZ(transform.position, _settings.AreaRadius,
            _settings.SectorCenterDegrees, _settings.SectorAngleDegrees, 64);

        switch (_drawStage)
        {
            case Stage.Delaunay:
                DrawEdges(_delaunayEdges, new Color(0.6f, 0.6f, 0.6f, 0.9f));
                break;

            case Stage.Tree:
                DrawEdges(_delaunayEdges, new Color(0.4f, 0.4f, 0.4f, 0.2f)); // 배경: 전체 삼각망(희미)
                DrawEdges(_treeEdges, new Color(0.3f, 0.9f, 0.4f, 1f));       // 트리 = 초록
                break;

            case Stage.Pruned:
                DrawEdges(_delaunayEdges, new Color(0.4f, 0.4f, 0.4f, 0.15f)); // 버려진 엣지(희미)
                DrawEdges(_treeEdges, new Color(0.3f, 0.9f, 0.4f, 1f));        // 트리 = 초록
                DrawExtraEdges();                                              // 추가 유지 = 시안
                break;
        }

        // 본진 = 노랑(크게), 나머지 = 흰색
        for (int i = 0; i < _positions.Count; i++)
        {
            Gizmos.color = i == 0 ? new Color(1f, 0.85f, 0.2f) : Color.white;
            Gizmos.DrawSphere(ToWorld(_positions[i]), i == 0 ? 0.5f : 0.25f);
        }
    }

    private void DrawEdges(List<TerritoryEdge> edges, Color color)
    {
        if (edges == null)
        {
            return;
        }

        Gizmos.color = color;
        for (int i = 0; i < edges.Count; i++)
        {
            Gizmos.DrawLine(ToWorld(_positions[edges[i].A]), ToWorld(_positions[edges[i].B]));
        }
    }

    // 최종 엣지 중 트리에 없는 것(프루닝에서 살아남은 사이클 재료)만 시안으로.
    private void DrawExtraEdges()
    {
        if (_prunedEdges == null || _treeEdges == null)
        {
            return;
        }

        var inTree = new HashSet<TerritoryEdge>(_treeEdges);
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 1f);
        for (int i = 0; i < _prunedEdges.Count; i++)
        {
            if (!inTree.Contains(_prunedEdges[i]))
            {
                Gizmos.DrawLine(ToWorld(_positions[_prunedEdges[i].A]), ToWorld(_positions[_prunedEdges[i].B]));
            }
        }
    }

    private static void DrawSectorXZ(Vector3 center, float radius, float centerDeg, float angleDeg, int segments)
    {
        float startDeg = centerDeg - angleDeg * 0.5f;

        Vector3 prev = center + DirXZ(startDeg) * radius;
        if (angleDeg < 360f)
        {
            Gizmos.DrawLine(center, prev); // 시작 반지름
        }

        for (int i = 1; i <= segments; i++)
        {
            Vector3 next = center + DirXZ(startDeg + angleDeg * i / segments) * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        if (angleDeg < 360f)
        {
            Gizmos.DrawLine(center, prev); // 끝 반지름
        }
    }

    private static Vector3 DirXZ(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
    }
}
