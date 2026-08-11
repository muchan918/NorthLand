using UnityEngine;

/// 주민이 **멈춰 서면 안 되는 구역**(#332). 빈 GameObject에 붙여 다리·계단·좁은 골목에 심는다.
///
/// ── 왜 "대화 금지"가 아니라 "정지 금지"인가 ──────────────────────
///
/// 막고 싶은 것은 대화라는 행위가 아니라 **좁은 곳에서 멈춰 서는 것**이다. #305에서 대화 회피물을
/// 폐기하며 "지나가는 주민이 참가자 사이로 지나가는 그림은 감수한다"고 정했는데, 통로 폭이 참가자
/// 원 지름(2인 4.0 / 3인 4.62)보다 좁으면 그 절충이 성립하지 않는다. 같은 자리에서 R5 춤도,
/// 이후 §10 공연도 똑같이 길을 막는다 — 그래서 컴포넌트는 하나고 소비처만 늘린다.
///
/// ── 왜 콜라이더가 아닌가 ───────────────────────────────────────
///
/// <see cref="ResidentRegistry"/>가 이미 "주민은 앰비언트 캐릭터라 전용 레이어·콜라이더 규약이 없어
/// 물리 질의를 쓰지 않는다"를 정해 두었다. 트리거 콜라이더를 도입하면 레이어를 새로 파야 하고,
/// `Physics.queriesHitTriggers` 기본값 때문에 **레이캐스트에 잡히므로** `MouseManager`의 선택·배치
/// 마스크와 스킬 타게팅에서 그 레이어를 빠짐없이 빼는 것이 앞으로 계속 지켜야 할 암묵 규약이 된다.
/// 얻는 것은 씬 뷰 핸들 하나뿐인데, 그건 <c>BoxBoundsHandle</c>로 30줄이면 만든다
/// (<c>ResidentNoStopZoneEditor</c>).
///
/// 그래서 <see cref="ResidentWaypoint"/>와 같은 모양이다 — 씬에 심는 저작용 오브젝트, 스스로 등록/해제,
/// 소비처는 레지스트리를 선형 순회.
///
/// ── 판정은 3D 상자다 ──────────────────────────────────────────
///
/// 주민 시스템의 다른 질의는 전부 Y를 무시하지만(계단·언덕에서 옆에 선 주민이 반경 밖으로 밀려나므로)
/// **여기서는 Y를 센다.** 초콜릿 다리는 지면(2.99) 위 4.49에 떠 있어(§11.11), Y를 무시하면 다리 밑을
/// 지나는 통로까지 함께 막힌다. 대신 배치자가 **걷는 면을 확실히 덮도록** 상자를 넉넉히 그려야 한다.
[AddComponentMenu("NorthLand/Resident/Resident No Stop Zone")]
public class ResidentNoStopZone : MonoBehaviour
{
    [Tooltip("상자의 중심(로컬 좌표). 씬 뷰의 면 핸들을 끌어 조정한다.")]
    [SerializeField] private Vector3 center = Vector3.zero;

    [Tooltip("상자의 크기(로컬 좌표). 씬 뷰의 면 핸들을 끌어 조정한다.")]
    [SerializeField] private Vector3 size = new Vector3(4f, 4f, 4f);

    [Tooltip("끄면 판정에서 제외된다. 오브젝트를 지우지 않고 잠시 빼 볼 때 쓴다.")]
    [SerializeField] private bool active = true;

    public Vector3 Center => center;

    public Vector3 Size => size;

    /// 판정에 쓸 수 있는 상태인가. 레지스트리는 비활성 항목도 들고 있으므로 소비처가 이걸 본다
    /// (<see cref="ResidentWaypoint.IsUsable"/>과 같은 규칙).
    public bool IsUsable => active && isActiveAndEnabled && size.x > 0f && size.y > 0f && size.z > 0f;

    private void OnEnable() => ResidentNoStopZoneRegistry.Register(this);

    private void OnDisable() => ResidentNoStopZoneRegistry.Unregister(this);

    /// 에디터의 씬 뷰 핸들이 부르는 유일한 창구. 음수 크기는 접어 둔다 — 핸들을 반대편으로 끌어
    /// 넘기면 크기가 음수가 되고, 그대로 두면 <see cref="Contains"/>가 아무것도 포함하지 않는
    /// 상자가 되어 **존이 조용히 죽는다.**
    public void SetBounds(Vector3 newCenter, Vector3 newSize)
    {
        center = newCenter;
        size = new Vector3(Mathf.Abs(newSize.x), Mathf.Abs(newSize.y), Mathf.Abs(newSize.z));
    }

    /// 이 지점이 상자 안인가. 트랜스폼의 회전·스케일을 그대로 타므로 **비스듬한 통로에 맞출 수 있다**
    /// (오브젝트를 돌리면 상자가 함께 돈다).
    public bool Contains(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint) - center;
        Vector3 half = size * 0.5f;

        return Mathf.Abs(local.x) <= half.x
            && Mathf.Abs(local.y) <= half.y
            && Mathf.Abs(local.z) <= half.z;
    }

    // ── 기즈모 ─────────────────────────────
    // 빈 GameObject라 아이콘이 없으면 씬에서 보이지 않는다. 항상 그리되, 선택 시에만 진하게 한다
    // (ResidentWaypoint와 같은 규칙 — 배치자가 존을 찾으려고 하이어라키를 뒤지지 않아도 된다).

    private void OnDrawGizmos()
    {
        DrawGizmo(IsUsable ? new Color(1f, 0.35f, 0.3f, 0.25f) : new Color(0.5f, 0.5f, 0.5f, 0.2f), false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawGizmo(IsUsable ? new Color(1f, 0.4f, 0.35f, 0.9f) : new Color(0.6f, 0.6f, 0.6f, 0.6f), true);
    }

    private void DrawGizmo(Color color, bool filled)
    {
        if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
        {
            return;
        }

        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (filled)
        {
            // 면을 옅게 채워야 상자가 통로를 어디까지 덮는지 눈으로 읽힌다. 와이어만으로는
            // 다리처럼 기울어진 지형 위에서 앞뒤 면이 겹쳐 보여 판단이 안 된다.
            Gizmos.color = new Color(color.r, color.g, color.b, 0.12f);
            Gizmos.DrawCube(center, size);
        }

        Gizmos.color = color;
        Gizmos.DrawWireCube(center, size);

        Gizmos.matrix = previous;
    }
}
