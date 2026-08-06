using UnityEngine;
using UnityEngine.AI;

/// 주민이 이동할 목적지 영역(#276, R2 산책). 빈 GameObject에 붙여 씬에 심는다.
///
/// 왜 "점"이 아니라 "영역"인가: 목적지가 점이면 주민 전원이 같은 좌표로 모여 겹친다. 반경 안에서 매번
/// 다른 지점을 뽑으면 같은 장소를 향하면서도 서로 다른 자리에 선다 — 광장·시장처럼 "사람이 모이는 곳"이
/// 자연스럽게 표현된다.
///
/// 배치자가 씬에서 반경을 눈으로 보고 조정할 수 있게 기즈모를 그린다. 값 하나(Radius)뿐이라
/// 인스펙터만으로 저작이 끝난다.
[AddComponentMenu("NorthLand/Resident/Resident Waypoint")]
public class ResidentWaypoint : MonoBehaviour
{
    /// NavMesh 위로 끌어올 때 허용하는 최대 거리. 반경 안 표본이 살짝 빗나가도 이 안에 폴리곤이 있으면 붙인다.
    private const float SampleSnapDistance = 2f;

    /// 표본 재시도 횟수. 반경 전체가 NavMesh 위라면 1회로 끝나고, 가장자리에 걸친 영역에서만 반복된다.
    private const int SampleAttempts = 12;

    [Tooltip("이 지점 주변 몇 유닛 안에서 목적지를 뽑을지. 기즈모로 씬에 표시된다.")]
    [SerializeField, Range(1f, 20f)] private float radius = 8f;

    [Tooltip("끄면 목적지 후보에서 제외된다. 오브젝트를 지우지 않고 잠시 빼 볼 때 쓴다.")]
    [SerializeField] private bool active = true;

    public float Radius => radius;

    /// 후보로 쓸 수 있는 상태인가. 레지스트리는 비활성 항목도 들고 있으므로 소비처가 이걸 본다 —
    /// 등록/해제를 토글마다 반복하는 것보다 목록이 안정적이다.
    public bool IsUsable => active && isActiveAndEnabled && radius > 0f;

    private void OnEnable() => ResidentWaypointRegistry.Register(this);

    private void OnDisable() => ResidentWaypointRegistry.Unregister(this);

    /// 반경 안의 NavMesh 지점을 하나 돌려준다. 주민 BT가 목적지를 받아 가는 유일한 창구다.
    ///
    /// ⚠ 원판(XZ)에서 뽑는다. insideUnitSphere를 쓰면 Y 성분 때문에 후보가 지면 위아래로 떠서
    ///   스냅 거리를 넘긴 것이 전부 실패하고, **반경을 키울수록 실패율이 오르는** 동작이 된다.
    public bool TryGetRandomPoint(out Vector3 point)
    {
        point = transform.position;

        if (radius <= 0f)
        {
            // 반경 0은 "이 지점 정확히"라는 뜻이다. 그래도 NavMesh 위로는 올려서 돌려준다.
            if (!NavMesh.SamplePosition(point, out NavMeshHit center, SampleSnapDistance, NavMesh.AllAreas))
            {
                return false;
            }

            point = center.position;
            return true;
        }

        for (int i = 0; i < SampleAttempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, SampleSnapDistance, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        }

        return false;
    }

    // ── 기즈모 ─────────────────────────────
    // 빈 GameObject라 아이콘이 없으면 씬에서 보이지 않는다. 항상 그리되, 선택 시에만 진하게 한다.

    private void OnDrawGizmos()
    {
        DrawGizmo(IsUsable ? new Color(0.3f, 0.8f, 1f, 0.35f) : new Color(0.5f, 0.5f, 0.5f, 0.25f));
    }

    private void OnDrawGizmosSelected()
    {
        DrawGizmo(IsUsable ? new Color(0.3f, 0.9f, 1f, 0.9f) : new Color(0.6f, 0.6f, 0.6f, 0.6f));
    }

    private void DrawGizmo(Color color)
    {
        Gizmos.color = color;

        // 중심 표식 — 반경이 0이어도 위치는 보여야 한다.
        Gizmos.DrawSphere(transform.position, 0.25f);

        if (radius <= 0f)
        {
            return;
        }

        // XZ 평면의 원. Gizmos.DrawWireSphere는 구라서 지면 영역으로 읽히지 않는다.
        const int segments = 48;
        Vector3 previous = transform.position + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 current = transform.position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, current);
            previous = current;
        }
    }
}
