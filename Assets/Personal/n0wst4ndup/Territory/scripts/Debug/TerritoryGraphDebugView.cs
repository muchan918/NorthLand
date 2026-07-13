using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 생성기 검증용 샌드박스 컴포넌트 (씬: TerritoryGraphSandbox.unity) — 게임 로직과 무관한 개발 도구.<br/>
/// 인스펙터 값이 바뀔 때마다 재생성해 <b>씬 뷰 Gizmo</b>로 그리고(플레이 불필요),
/// 컴포넌트 우클릭 컨텍스트 메뉴로 결정성·불변식을 코드로도 검증한다.<br/>
/// 검증 실패는 LogError로 남겨 콘솔 에러 검사에 걸리게 한다.
/// </summary>
public class TerritoryGraphDebugView : MonoBehaviour
{
    // 파이프라인 단계별 산출물 비교용 (이후 Tree/Pruned 단계 추가 예정)
    private enum Stage
    {
        Scatter,
        Delaunay,
    }

    [SerializeField] TerritoryGraphGenSettings _settings = new();

    [Tooltip("생성 시드. 같은 (설정, 시드)는 항상 같은 결과 — 바꾸면 새 지형")]
    [SerializeField] int _seed = 12345;

    [Tooltip("어느 단계까지의 산출물을 그릴지 (Scatter=점만, Delaunay=삼각망)")]
    [SerializeField] Stage _drawStage = Stage.Delaunay;

    private List<Vector2> _positions;
    private List<TerritoryEdge> _edges;

    [ContextMenu("재생성")]
    public void Regenerate()
    {
        Generate(out _positions, out _edges);
        Debug.Log($"[영토검증] 생성: 노드 {_positions.Count}개, Delaunay 엣지 {_edges.Count}개, " +
                  $"최소 쌍거리 {MinPairDistance():F2} (설정 간격 {_settings.MinNodeSpacing:F2}, seed={_seed})");
    }

    /// <summary>같은 시드 2회 생성 → 위치·엣지 완전 일치해야 한다(WL-008 시드 재현성).</summary>
    [ContextMenu("결정성 검증")]
    public void VerifyDeterminism()
    {
        Generate(out List<Vector2> posA, out List<TerritoryEdge> edgeA);
        Generate(out List<Vector2> posB, out List<TerritoryEdge> edgeB);

        bool same = posA.Count == posB.Count && edgeA.Count == edgeB.Count;
        for (int i = 0; same && i < posA.Count; i++)
        {
            // Vector2 ==는 근사 비교(오차 허용)라 쓰지 않는다 — 결정성은 비트 단위 동일이어야 한다.
            same = posA[i].x == posB[i].x && posA[i].y == posB[i].y;
        }
        for (int i = 0; same && i < edgeA.Count; i++)
        {
            same = edgeA[i].Equals(edgeB[i]);
        }

        if (same)
        {
            Debug.Log($"[영토검증] 결정성 PASS — 같은 시드({_seed}) 2회 생성 완전 일치 " +
                      $"(노드 {posA.Count}, 엣지 {edgeA.Count})");
        }
        else
        {
            Debug.LogError($"[영토검증] 결정성 FAIL — 같은 시드({_seed})인데 결과가 다릅니다!");
        }
    }

    /// <summary>불변식: 본진=원점 / 반지름 내 / 최소 간격 + 평면성(교차 0) / 연결성(본진 전 노드 도달).</summary>
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

        // ── 평면성: 어떤 엣지 쌍도 내부에서 교차하지 않는다 (§4.1 평면 그래프) ──
        int crossings = 0;
        for (int i = 0; i < _edges.Count; i++)
        {
            for (int j = i + 1; j < _edges.Count; j++)
            {
                TerritoryEdge e1 = _edges[i];
                TerritoryEdge e2 = _edges[j];
                if (e1.A == e2.A || e1.A == e2.B || e1.B == e2.A || e1.B == e2.B)
                {
                    continue;
                }
                if (TerritoryGeometry.SegmentsProperlyIntersect(
                        _positions[e1.A], _positions[e1.B], _positions[e2.A], _positions[e2.B]))
                {
                    Debug.LogError($"[영토검증] FAIL: 엣지 교차 {e1} × {e2}");
                    crossings++;
                }
            }
        }
        fail += crossings;

        // ── 연결성: 본진(0)에서 모든 노드 도달 (§4.1 스패닝 트리 보존의 전제) ──
        if (!TerritoryGraphGenerator.IsConnected(_edges, _positions.Count))
        {
            Debug.LogError("[영토검증] FAIL: 본진에서 도달 불가한 노드가 있습니다.");
            fail++;
        }

        if (fail == 0)
        {
            Debug.Log($"[영토검증] 불변식 PASS (노드 {_positions.Count}, 엣지 {_edges.Count}, " +
                      $"교차 0, 연결성 OK, 최소 쌍거리 {minDist:F2})");
        }
    }

    // 산포→삼각분할 파이프라인 1회 실행. rng 소비 순서(산포가 전부 소비, 삼각분할은 순수)가 여기서 고정된다.
    private void Generate(out List<Vector2> positions, out List<TerritoryEdge> edges)
    {
        positions = TerritoryGraphGenerator.ScatterPositions(_settings, new System.Random(_seed));
        edges = TerritoryGraphGenerator.Triangulate(positions);
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
        Generate(out _positions, out _edges);
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

        // Delaunay 엣지 (회색)
        if (_drawStage >= Stage.Delaunay && _edges != null)
        {
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.9f);
            for (int i = 0; i < _edges.Count; i++)
            {
                Gizmos.DrawLine(ToWorld(_positions[_edges[i].A]), ToWorld(_positions[_edges[i].B]));
            }
        }

        // 본진 = 노랑(크게), 나머지 = 흰색
        for (int i = 0; i < _positions.Count; i++)
        {
            Gizmos.color = i == 0 ? new Color(1f, 0.85f, 0.2f) : Color.white;
            Gizmos.DrawSphere(ToWorld(_positions[i]), i == 0 ? 0.5f : 0.25f);
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
