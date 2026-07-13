using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 영토 그래프 절차 생성기 (TerritoryGraph.md §4.1: ① 위치 산포 → ② Delaunay 삼각분할 → ③ 프루닝).<br/>
/// 순수 정적 함수 집합 — 모델(<see cref="TerritoryGraph"/>)을 모르고 그래프의 "형태"(위치·엣지)만 만든다.
/// 모델 조립(노드 생성·상태 초기화·영토 배정)은 호출자의 몫.<br/>
/// <br/>
/// <b>랜덤 규율 (WL-008 재발 방지)</b>:<br/>
/// - 전역 상태인 UnityEngine.Random을 절대 쓰지 않는다.<br/>
/// - 시드로 만든 System.Random 인스턴스를 주입받고, 소비 순서를 고정한다(산포 → 트리 → 추가 엣지).<br/>
/// - 따라서 같은 (설정, 시드)는 항상 같은 결과 — 로그라이크 런 시드 재현의 전제.
/// </summary>
public static class TerritoryGraphGenerator
{
    // 후보 1개당 최대 거부 횟수. 초과 시 간격을 완화한다(무한 루프 방지).
    private const int MaxAttemptsPerNode = 30;

    /// <summary>
    /// ① 위치 산포 — rejection sampling. 본진(index 0)은 원점 고정(rng 소비 없음),
    /// 나머지는 원 안에 최소 간격을 지키며 배치한다.<br/>
    /// 바깥 편향은 반지름 분포의 지수로 제어한다: 원 내부 '면적 균일'은 r = R·√u 인데,
    /// 지수를 열어 r = R·u^(1/(2·bias))로 두면 bias&gt;1일수록 바깥 고리에 몰린다.
    /// </summary>
    public static List<Vector2> ScatterPositions(TerritoryGraphGenSettings settings, System.Random rng)
    {
        var positions = new List<Vector2>();
        if (settings == null || rng == null)
        {
            Debug.LogError("[영토생성] 설정/난수원이 null이라 산포할 수 없습니다.");
            return positions;
        }

        int nodeCount = Mathf.Clamp(settings.NodeCount, 1, 30);
        if (nodeCount != settings.NodeCount)
        {
            Debug.LogWarning($"[영토생성] 노드 수를 {settings.NodeCount} → {nodeCount}로 클램프했습니다 (GDD 상한 30).");
        }

        WarnIfInfeasible(settings, nodeCount);

        positions.Add(Vector2.zero); // 본진

        float spacing = Mathf.Max(0f, settings.MinNodeSpacing);
        float spacingFloor = settings.AreaRadius * 0.01f;

        while (positions.Count < nodeCount)
        {
            if (TryPlaceOne(settings, rng, positions, spacing, out Vector2 placed))
            {
                positions.Add(placed);
                continue;
            }

            // 연속 거부 → 간격 완화. 같은 rng 흐름 안에서 일어나므로 결과는 여전히 결정적이다.
            spacing *= 0.9f;
            Debug.LogWarning($"[영토생성] 최소 간격을 만족하지 못해 완화합니다: {spacing:F2}");

            if (spacing < spacingFloor)
            {
                // 하드 플로어: 설정이 물리적으로 불가능한 경우의 최후 수단 — 간격 검사 없이 채운다.
                Debug.LogWarning("[영토생성] 간격 하한 도달 — 남은 노드는 간격 검사 없이 배치합니다.");
                while (positions.Count < nodeCount)
                {
                    positions.Add(SamplePosition(settings, rng));
                }
            }
        }

        return positions;
    }

    private static bool TryPlaceOne(TerritoryGraphGenSettings settings, System.Random rng,
        List<Vector2> existing, float spacing, out Vector2 placed)
    {
        for (int attempt = 0; attempt < MaxAttemptsPerNode; attempt++)
        {
            Vector2 candidate = SamplePosition(settings, rng);
            if (IsFarEnough(candidate, existing, spacing))
            {
                placed = candidate;
                return true;
            }
        }

        placed = default;
        return false;
    }

    // 극좌표 샘플 1회 — rng를 정확히 2번 소비한다(반지름, 각도).
    // 각도는 부채꼴 범위로 제한한다: 본진(꼭짓점)에서 한쪽으로만 퍼지는 GDD 공간 구조
    // (전투 영역 ↔ 본진 ↔ 경영 확장 공간). 360°면 전방향(원)과 동일.
    private static Vector2 SamplePosition(TerritoryGraphGenSettings settings, System.Random rng)
    {
        float bias = Mathf.Max(0.01f, settings.OutwardBias);
        float u = (float)rng.NextDouble();
        float r = settings.AreaRadius * Mathf.Pow(u, 1f / (2f * bias));

        float halfAngleRad = settings.SectorAngleDegrees * 0.5f * Mathf.Deg2Rad;
        float centerRad = settings.SectorCenterDegrees * Mathf.Deg2Rad;
        float theta = centerRad + ((float)rng.NextDouble() * 2f - 1f) * halfAngleRad;

        return new Vector2(r * Mathf.Cos(theta), r * Mathf.Sin(theta));
    }

    private static bool IsFarEnough(Vector2 candidate, List<Vector2> existing, float spacing)
    {
        float sqrSpacing = spacing * spacing;
        for (int i = 0; i < existing.Count; i++)
        {
            if ((existing[i] - candidate).sqrMagnitude < sqrSpacing)
            {
                return false;
            }
        }
        return true;
    }

    // 대략적 실현 가능성 추정: 노드가 점유하는 원(지름=간격)의 면적 합이 산포 부채꼴의 절반을 넘으면
    // rejection sampling의 거부율이 급증한다 — 튜닝 실수를 완화 경고 폭탄 전에 미리 알려준다.
    private static void WarnIfInfeasible(TerritoryGraphGenSettings settings, int nodeCount)
    {
        float required = nodeCount * Mathf.PI * Mathf.Pow(settings.MinNodeSpacing * 0.5f, 2f);
        // 부채꼴 면적 = (θ/2)·R² (θ는 라디안). 360°면 원 전체와 동일.
        float sectorRad = Mathf.Clamp(settings.SectorAngleDegrees, 10f, 360f) * Mathf.Deg2Rad;
        float available = 0.5f * (0.5f * sectorRad * settings.AreaRadius * settings.AreaRadius);
        if (required > available)
        {
            Debug.LogWarning(
                $"[영토생성] 설정이 빡빡합니다 (필요 면적 {required:F0} > 여유 {available:F0}) — 간격 완화가 자주 발생할 수 있습니다.");
        }
    }

    // ── ② Delaunay 삼각분할 ─────────────────────────────────────────────

    // 근사 퇴화(공선) 판정 임계값. 좌표 스케일 ~10에서 사실상 '정확히 일직선'만 걸러낸다.
    private const double DegenerateEps = 1e-9;

    /// <summary>
    /// ② Delaunay 삼각분할 — 정의 기반 전수 검사(brute-force).<br/>
    /// Delaunay의 정의는 "외접원 내부에 다른 점이 없는 삼각형들의 집합"이다. 모든 삼중조 (i,j,k)에
    /// 대해 이 정의를 그대로 검사한다. O(n⁴)이지만 n≤30이면 ~11만 판정(ms 단위)이고 런 시작 시 1회뿐 —
    /// 대신 증분 알고리즘(Bowyer-Watson)의 super-triangle 크기·경계 복원 함정이 전부 사라진다.
    /// 결과 엣지 집합은 동일하다.<br/>
    /// 출력은 (A,B) 오름차순 정렬. rng를 소비하지 않는 순수 함수 — 파이프라인 결정성 논증이 단순해진다.
    /// </summary>
    public static List<TerritoryEdge> Triangulate(IReadOnlyList<Vector2> points)
    {
        var edges = new List<TerritoryEdge>();
        if (points == null || points.Count <= 1)
        {
            return edges;
        }

        if (points.Count == 2)
        {
            edges.Add(new TerritoryEdge(0, 1));
            return edges;
        }

        int n = points.Count;
        var unique = new HashSet<TerritoryEdge>();

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                for (int k = j + 1; k < n; k++)
                {
                    double orient = TerritoryGeometry.Orientation(points[i], points[j], points[k]);
                    if (Math.Abs(orient) < DegenerateEps)
                    {
                        continue; // 퇴화(공선) 삼중조 — 외접원이 정의되지 않는다
                    }

                    // InCircle의 부호 규약은 a,b,c가 CCW일 때 성립 — 방향을 정규화한다.
                    Vector2 a = points[i];
                    Vector2 b = orient > 0 ? points[j] : points[k];
                    Vector2 c = orient > 0 ? points[k] : points[j];

                    bool empty = true;
                    for (int m = 0; m < n && empty; m++)
                    {
                        if (m == i || m == j || m == k)
                        {
                            continue;
                        }
                        if (TerritoryGeometry.InCircle(a, b, c, points[m]) > 0)
                        {
                            empty = false;
                        }
                    }

                    if (empty)
                    {
                        unique.Add(new TerritoryEdge(i, j));
                        unique.Add(new TerritoryEdge(j, k));
                        unique.Add(new TerritoryEdge(i, k));
                    }
                }
            }
        }

        edges.AddRange(unique);
        edges.Sort(); // (A,B) 오름차순 — 이후 rng 소비 루프의 순회 순서 고정(결정성)

        RemoveCrossings(edges, points); // 안전망 1: 근사 공원(cocircular) 4점 → 교차 엣지 제거
        EnsureConnected(edges, points); // 안전망 2: 전점 공선 등 퇴화 입력 → EMST 대체

        return edges;
    }

    /// <summary>본진(0)에서 모든 노드에 도달 가능한가 — BFS. 검증·프루닝 재검에도 쓴다.</summary>
    public static bool IsConnected(IReadOnlyList<TerritoryEdge> edges, int nodeCount)
    {
        if (nodeCount <= 1)
        {
            return true;
        }

        var adjacency = new List<int>[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            adjacency[i] = new List<int>();
        }
        for (int i = 0; i < edges.Count; i++)
        {
            adjacency[edges[i].A].Add(edges[i].B);
            adjacency[edges[i].B].Add(edges[i].A);
        }

        var visited = new bool[nodeCount];
        var queue = new Queue<int>();
        visited[0] = true;
        queue.Enqueue(0);
        int reached = 1;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            List<int> neighbors = adjacency[current];
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (!visited[neighbors[i]])
                {
                    visited[neighbors[i]] = true;
                    reached++;
                    queue.Enqueue(neighbors[i]);
                }
            }
        }

        return reached == nodeCount;
    }

    // 안전망 1 — 네 점이 거의 한 원 위(cocircular)면 두 대각선이 모두 "빈 외접원"을 통과해
    // 교차 엣지가 생길 수 있다. 교차 쌍에서 긴 쪽을 제거한다(동률이면 (A,B) 큰 쪽 — 결정적 타이브레이크).
    private static void RemoveCrossings(List<TerritoryEdge> edges, IReadOnlyList<Vector2> points)
    {
        bool removedAny = true;
        while (removedAny)
        {
            removedAny = false;
            for (int i = 0; i < edges.Count && !removedAny; i++)
            {
                for (int j = i + 1; j < edges.Count && !removedAny; j++)
                {
                    TerritoryEdge e1 = edges[i];
                    TerritoryEdge e2 = edges[j];
                    if (e1.A == e2.A || e1.A == e2.B || e1.B == e2.A || e1.B == e2.B)
                    {
                        continue; // 끝점 공유 쌍은 교차 대상이 아니다
                    }
                    if (!TerritoryGeometry.SegmentsProperlyIntersect(
                            points[e1.A], points[e1.B], points[e2.A], points[e2.B]))
                    {
                        continue;
                    }

                    float len1 = (points[e1.A] - points[e1.B]).sqrMagnitude;
                    float len2 = (points[e2.A] - points[e2.B]).sqrMagnitude;
                    int removeIndex = len1 > len2 ? i
                        : len2 > len1 ? j
                        : (e1.CompareTo(e2) > 0 ? i : j);

                    Debug.LogWarning($"[영토생성] 교차 엣지 제거(근사 공원 안전망): {edges[removeIndex]}");
                    edges.RemoveAt(removeIndex);
                    removedAny = true;
                }
            }
        }
    }

    // 안전망 2 — 삼각분할이 비었거나 비연결(전점 근사 공선 등 랜덤 입력에서 확률 ~0의 퇴화)이면
    // 유클리드 최소 신장 트리(Prim, O(n²))로 대체해 "본진에서 전 노드 도달"만은 항상 보장한다.
    private static void EnsureConnected(List<TerritoryEdge> edges, IReadOnlyList<Vector2> points)
    {
        int n = points.Count;
        if (n <= 2 || IsConnected(edges, n))
        {
            return;
        }

        Debug.LogWarning("[영토생성] 삼각분할 결과가 비연결 — EMST로 대체합니다(퇴화 입력 안전망).");
        edges.Clear();

        var inTree = new bool[n];
        var bestDist = new float[n];
        var bestFrom = new int[n];
        inTree[0] = true;
        for (int i = 1; i < n; i++)
        {
            bestDist[i] = (points[i] - points[0]).sqrMagnitude;
            bestFrom[i] = 0;
        }

        for (int step = 1; step < n; step++)
        {
            int next = -1;
            for (int i = 0; i < n; i++)
            {
                if (!inTree[i] && (next < 0 || bestDist[i] < bestDist[next]))
                {
                    next = i;
                }
            }

            inTree[next] = true;
            edges.Add(new TerritoryEdge(bestFrom[next], next));

            for (int i = 0; i < n; i++)
            {
                if (!inTree[i])
                {
                    float dist = (points[i] - points[next]).sqrMagnitude;
                    if (dist < bestDist[i])
                    {
                        bestDist[i] = dist;
                        bestFrom[i] = next;
                    }
                }
            }
        }

        edges.Sort();
    }
}
