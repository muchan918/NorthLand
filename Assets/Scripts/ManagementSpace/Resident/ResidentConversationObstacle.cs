using UnityEngine;
using UnityEngine.AI;

/// 대화 중인 무리를 지나가던 주민이 **밀고 지나가지 않도록** 세우는 회피물(§7.1).
///
/// 실측된 문제: 걸어가던 주민이 마주 서서 이야기하는 두 사람 사이로 파고들어 밀어낸다.
/// `NavMeshAgent`의 지역 회피는 **정지한 에이전트를 밀어내는 쪽**으로 풀리기 때문에, 대화 참가자끼리
/// 서로 안 밀어내는 것과 별개로 **바깥에서 들어오는 이동체**는 막지 못한다.
///
/// ── 왜 별도 GameObject인가 ──────────────────────────────────────
///
/// `NavMeshObstacle`을 `NavMeshAgent`와 같은 GameObject에 올리면 **자기가 만든 회피물에 자기가 밀려난다.**
/// Unity가 권장하지 않는 조합이다. 그래서 참가자마다 붙이지 않고 **세션당 하나**를 무리 중심에 세운다.
/// 인원이 2명이든 3명이든 오브젝트 수는 1이다.
///
/// ── 왜 carving을 끄는가 ────────────────────────────────────────
///
/// `carving = true`면 NavMesh에 실제로 구멍이 뚫려 경로가 우회하지만, **참가자가 그 구멍 안에 서 있게 되어
/// 오프메시가 된다** — 해산할 때 `TrySetDestination`이 실패한다. 끄면 NavMesh를 건드리지 않고
/// 지역 회피에만 참여하므로, 지나가던 주민은 돌아가고 참가자는 NavMesh 위에 그대로 남는다.
/// 국소 NavMesh 재계산 비용도 들지 않는다.
///
/// ── 수명 ────────────────────────────────────────────────────────
///
/// 정상 종료는 <see cref="ResidentConversation.Disband"/>가 <see cref="Release"/>로 거둔다. 다만 밤 수거처럼
/// **참가자 전원이 한꺼번에 비활성**되면 어느 노드도 해산을 처리하지 못해 이 오브젝트만 남는다 —
/// 그래서 스스로도 살아 있을 이유를 확인한다. 세션이 티커를 두지 않는 설계라 그 몫이 여기로 온다.
[DisallowMultipleComponent]
public class ResidentConversationObstacle : MonoBehaviour
{
    /// 무리 반지름에서 **빼는** 여유. 주민 반경(0.3)에 여백을 더한 값이다.
    ///
    /// ⚠ **더하면 안 된다.** 회피물이 참가자를 품으면 정지한 참가자들이 자기 회피물에서 벗어나려
    ///   움직여 **서로 엉겨 붙는다**(실측). 지역 회피는 정지한 에이전트도 밀어내기 때문이다.
    ///   막아야 하는 것은 참가자가 서 있는 자리가 아니라 **그 사이를 파고드는 경로**다.
    /// 참가자 **중심**까지만 덮게 빼는 값(주민 반경 0.3 + 여유). 참가자는 대화 중 자기 회피를 끄므로
    /// (`ResidentAgent.SetStationaryHold`) 이 정도까지 키워도 밀리지 않고, 대신 지나가던 주민이
    /// 참가자 사이로 빠져나갈 틈이 없어진다.
    private const float Clearance = 0.35f;

    /// 안쪽 반지름이 이보다 작으면 막을 공간이 없다고 보고 끈다. 다가가기 상한에 걸려 바짝 붙어
    /// 이야기하는 경우가 여기 걸린다 — 그때는 참가자 본체가 이미 길을 막는다.
    private const float MinRadius = 0.35f;

    /// 회피물의 높이. 주민 키(1.63)를 덮으면 충분하다.
    private const float Height = 2f;

    private ResidentConversation session;
    private NavMeshObstacle obstacle;

    public static ResidentConversationObstacle Create(ResidentConversation session)
    {
        if (session == null)
        {
            return null;
        }

        var go = new GameObject("ResidentConversationObstacle");
        var view = go.AddComponent<ResidentConversationObstacle>();

        view.session = session;
        view.obstacle = go.AddComponent<NavMeshObstacle>();
        view.obstacle.shape = NavMeshObstacleShape.Capsule;
        view.obstacle.carving = false;   // NavMesh를 건드리지 않는다 — 위 주석 참조
        view.Follow();

        return view;
    }

    /// 세션이 정상적으로 끝났을 때 거둔다.
    public void Release()
    {
        session = null;

        if (this != null)
        {
            Destroy(gameObject);
        }
    }

    private void LateUpdate()
    {
        // 세션이 끝났거나 참가자가 아무도 안 남았으면 존재할 이유가 없다.
        if (session == null
            || session.Phase == ResidentConversation.ConversationPhase.Ended
            || session.ActiveParticipantCount == 0)
        {
            Destroy(gameObject);
            return;
        }

        Follow();
    }

    /// 무리의 중심·크기를 따라간다. 참가자가 자리를 미세하게 고치거나 합류로 원이 커질 수 있다.
    private void Follow()
    {
        if (session == null || obstacle == null)
        {
            return;
        }

        if (!session.TryGetCircle(out Vector3 center, out float radius))
        {
            obstacle.enabled = false;
            return;
        }

        transform.position = center;

        // **이야기하는 동안만** 켠다. 인사·다가가기 중에 켜면 자리를 잡으려 걸어가는 참가자들이
        // 자기 회피물을 피해 흩어진다.
        float inner = radius - Clearance;
        bool shouldBlock = session.Phase == ResidentConversation.ConversationPhase.Talking && inner >= MinRadius;

        obstacle.enabled = shouldBlock;

        if (!shouldBlock)
        {
            return;
        }

        obstacle.radius = inner;

        // 중심을 절반만큼 올린다. 세션 좌표는 **발치**인데 캡슐은 중심 기준으로 위아래 대칭이라,
        // 올리지 않으면 절반이 지면 아래로 들어가 주민 몸통(키 1.63)을 덜 덮는다.
        //
        // ⚠ `NavMeshObstacle.height`는 **half-height 게터다** — `height = 2`를 넣으면 `size.y = 2`가 되고
        //   되읽으면 1이 나온다. 1로 보인다고 다시 2를 넣지 말 것(실제 높이가 4가 된다).
        obstacle.height = Height;
        obstacle.center = new Vector3(0f, Height * 0.5f, 0f);
    }
}
