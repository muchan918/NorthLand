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
    private static Vector2 SamplePosition(TerritoryGraphGenSettings settings, System.Random rng)
    {
        float bias = Mathf.Max(0.01f, settings.OutwardBias);
        float u = (float)rng.NextDouble();
        float r = settings.AreaRadius * Mathf.Pow(u, 1f / (2f * bias));
        float theta = (float)(rng.NextDouble() * Math.PI * 2.0);
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

    // 대략적 실현 가능성 추정: 노드가 점유하는 원(지름=간격)의 면적 합이 산포 원의 절반을 넘으면
    // rejection sampling의 거부율이 급증한다 — 튜닝 실수를 완화 경고 폭탄 전에 미리 알려준다.
    private static void WarnIfInfeasible(TerritoryGraphGenSettings settings, int nodeCount)
    {
        float required = nodeCount * Mathf.PI * Mathf.Pow(settings.MinNodeSpacing * 0.5f, 2f);
        float available = 0.5f * Mathf.PI * settings.AreaRadius * settings.AreaRadius;
        if (required > available)
        {
            Debug.LogWarning(
                $"[영토생성] 설정이 빡빡합니다 (필요 면적 {required:F0} > 여유 {available:F0}) — 간격 완화가 자주 발생할 수 있습니다.");
        }
    }
}
