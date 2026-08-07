using UnityEngine;

/// 주민이 드나드는 문 앞 지점(#276, §4 · R8 귀가 · R9 등장). 빈 GameObject에 붙여 집·생산 건물의 문 앞에 심는다.
///
/// **+Z가 문에서 나가는 방향을 보게 놓는다.** 이 전방 벡터가 등장 시 직진 방향이다(§3.2 퇴장 유예).
/// 방향이 뒤집혀 있으면 주민이 나오자마자 벽으로 걸어간다 — 기즈모가 화살표로 그 방향을 보여 준다.
///
/// **"집"이라는 개념을 따로 두지 않는다**(§4). 포인트가 박혀 있으면 그곳이 곧 들어갈 수 있는 곳이고,
/// 밤이 되면 주민은 가장 가까운 포인트로 갈 뿐 건물 타입·태그·소유 관계를 판정하지 않는다.
[AddComponentMenu("NorthLand/Resident/Resident Door Point")]
public class ResidentDoorPoint : MonoBehaviour
{
    [Tooltip("끄면 밤 목적지·아침 등장 후보에서 제외된다. 오브젝트를 지우지 않고 잠시 빼 볼 때 쓴다.")]
    [SerializeField] private bool active = true;

    public Vector3 Position => transform.position;

    /// 문에서 나가는 방향(+Z). 수평 성분만 쓴다 — 문이 기울어 배치돼도 주민은 지면을 걷는다.
    public Vector3 Forward
    {
        get
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;

            // 완전히 수직으로 눕혀 놓은 경우의 방어. 방향을 못 정하면 등장이 제자리걸음이 된다.
            return forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
        }
    }

    /// 후보로 쓸 수 있는 상태인가. 레지스트리는 비활성 항목도 들고 있으므로 소비처가 이걸 본다
    /// (<see cref="ResidentWaypoint"/>와 같은 방식).
    public bool IsUsable => active && isActiveAndEnabled;

    private void OnEnable() => ResidentDoorPointRegistry.Register(this);

    private void OnDisable() => ResidentDoorPointRegistry.Unregister(this);

    // ── 기즈모 ─────────────────────────────
    // 빈 GameObject라 아이콘이 없으면 씬에서 보이지 않는다. 방향이 이 앵커의 핵심이라 화살표로 그린다.

    private void OnDrawGizmos()
    {
        DrawGizmo(IsUsable ? new Color(1f, 0.75f, 0.2f, 0.5f) : new Color(0.5f, 0.5f, 0.5f, 0.3f));
    }

    private void OnDrawGizmosSelected()
    {
        DrawGizmo(IsUsable ? new Color(1f, 0.8f, 0.3f, 1f) : new Color(0.6f, 0.6f, 0.6f, 0.7f));
    }

    private void DrawGizmo(Color color)
    {
        const float ArrowLength = 3f;
        const float HeadLength = 0.5f;
        const float HeadWidth = 0.3f;

        Gizmos.color = color;
        Gizmos.DrawSphere(Position, 0.25f);

        Vector3 forward = Forward;
        Vector3 tip = Position + forward * ArrowLength;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Gizmos.DrawLine(Position, tip);
        Gizmos.DrawLine(tip, tip - forward * HeadLength + right * HeadWidth);
        Gizmos.DrawLine(tip, tip - forward * HeadLength - right * HeadWidth);
    }
}
