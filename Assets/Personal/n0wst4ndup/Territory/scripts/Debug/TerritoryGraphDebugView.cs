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
    [SerializeField] TerritoryGraphGenSettings _settings = new();

    [Tooltip("생성 시드. 같은 (설정, 시드)는 항상 같은 결과 — 바꾸면 새 지형")]
    [SerializeField] int _seed = 12345;

    private List<Vector2> _positions;

    [ContextMenu("재생성")]
    public void Regenerate()
    {
        _positions = TerritoryGraphGenerator.ScatterPositions(_settings, new System.Random(_seed));
        Debug.Log($"[영토검증] 산포: 노드 {_positions.Count}개, 최소 쌍거리 {MinPairDistance():F2} " +
                  $"(설정 간격 {_settings.MinNodeSpacing:F2}, seed={_seed})");
    }

    /// <summary>같은 시드 2회 생성 → 완전 일치해야 한다(WL-008 시드 재현성).</summary>
    [ContextMenu("결정성 검증")]
    public void VerifyDeterminism()
    {
        List<Vector2> a = TerritoryGraphGenerator.ScatterPositions(_settings, new System.Random(_seed));
        List<Vector2> b = TerritoryGraphGenerator.ScatterPositions(_settings, new System.Random(_seed));

        bool same = a.Count == b.Count;
        for (int i = 0; same && i < a.Count; i++)
        {
            // Vector2 ==는 근사 비교(오차 허용)라 쓰지 않는다 — 결정성은 비트 단위 동일이어야 한다.
            same = a[i].x == b[i].x && a[i].y == b[i].y;
        }

        if (same)
        {
            Debug.Log($"[영토검증] 결정성 PASS — 같은 시드({_seed}) 2회 생성 완전 일치 (노드 {a.Count}개)");
        }
        else
        {
            Debug.LogError($"[영토검증] 결정성 FAIL — 같은 시드({_seed})인데 결과가 다릅니다!");
        }
    }

    /// <summary>산포 불변식: 본진=원점 / 전 노드 반지름 안 / 최소 간격 준수.</summary>
    [ContextMenu("불변식 검증")]
    public void VerifyInvariants()
    {
        if (_positions == null || _positions.Count == 0)
        {
            Regenerate();
        }

        int fail = 0;

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

        // 간격 완화가 발생한 설정에서는 이 검사가 실패할 수 있다 — 완화 경고 로그와 함께 판단할 것.
        float minDist = MinPairDistance();
        if (minDist < _settings.MinNodeSpacing - 0.001f)
        {
            Debug.LogWarning($"[영토검증] 최소 쌍거리 {minDist:F2} < 설정 간격 {_settings.MinNodeSpacing:F2} " +
                             "— 간격 완화가 발생한 설정인지 확인하세요.");
        }

        if (fail == 0)
        {
            Debug.Log($"[영토검증] 불변식 PASS (노드 {_positions.Count}개, 최소 쌍거리 {minDist:F2})");
        }
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
        _positions = TerritoryGraphGenerator.ScatterPositions(_settings, new System.Random(_seed));
    }

    // 생성기는 2D(Vector2)로 일하고, 뷰가 XZ 평면에 얹는다 — 모델 조립 때도 동일 규약.
    private Vector3 ToWorld(Vector2 p) => transform.position + new Vector3(p.x, 0f, p.y);

    private void OnDrawGizmos()
    {
        if (_positions == null || _positions.Count == 0)
        {
            return;
        }

        // 산포 영역(원)
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        DrawCircleXZ(transform.position, _settings.AreaRadius, 64);

        // 본진 = 노랑(크게), 나머지 = 흰색
        for (int i = 0; i < _positions.Count; i++)
        {
            Gizmos.color = i == 0 ? new Color(1f, 0.85f, 0.2f) : Color.white;
            Gizmos.DrawSphere(ToWorld(_positions[i]), i == 0 ? 0.5f : 0.25f);
        }
    }

    private static void DrawCircleXZ(Vector3 center, float radius, int segments)
    {
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
