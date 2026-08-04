using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

// 목적지가 없으면 무작위 웨이포인트에서 하나 받아 온다(#276, R2 산책).
//
// 상태 흐름은 이렇다:
//   목적지가 있으면  → 그대로 둔다(이동 노드가 계속 그리로 간다)
//   목적지가 없으면  → 무작위 ResidentWaypoint를 골라 그 반경 안의 한 점을 받아 목적지로 삼는다
// 도착 시 해제는 ResidentMoveToAction이 한다.
//
// 목적지를 이미 들고 있으면 건드리지 않는 것이 중요하다. 그래야 나중에 다른 경로(R8 귀가·R9 등장·
// 드래그 후 복귀)가 목적지를 꽂아 두면 이 노드가 덮어쓰지 않고 그대로 존중한다.
//
// 반경 안에서 매번 다른 점을 뽑으므로, 같은 웨이포인트를 향한 주민들이 한 좌표에 겹치지 않는다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Resident Pick Waypoint Destination",
    description: "목적지가 없으면 무작위 ResidentWaypoint의 반경 안에서 한 점을 받아 목적지로 삼는다.",
    story: "[Agent] picks a destination from a random waypoint",
    category: "Action/Resident",
    id: "0b813127ec2f4b0ba3a867952b8113d1")]
public partial class ResidentPickWaypointDestinationAction : Action
{
    // 씬에 웨이포인트가 하나도 없을 때의 경고를 1회로 제한한다.
    // 주민 30명이 매 주기 경고를 뱉으면 콘솔이 묻혀 정작 원인을 못 찾는다.
    private static bool s_warnedNoWaypoint;

    [SerializeReference] public BlackboardVariable<ResidentAgent> Agent;

    // 뽑은 지점의 출력. ResidentMoveToAction이 같은 변수를 입력으로 받는다.
    [SerializeReference] public BlackboardVariable<Vector3> Destination;

    // 목적지 보유 여부. 이 노드가 세우고 이동 노드가 내린다.
    [SerializeReference] public BlackboardVariable<bool> HasDestination;

    protected override Status OnStart()
    {
        if (HasDestination == null || Destination == null)
        {
            LogFailure("Resident Pick Waypoint Destination: Destination / HasDestination 변수가 연결되지 않았습니다.");
            return Status.Failure;
        }

        // 이미 목적지가 있으면 그대로 둔다 — 덮어쓰면 걸어가던 주민이 매 주기 방향을 바꾼다.
        if (HasDestination.Value)
        {
            return Status.Success;
        }

        if (!ResidentWaypointRegistry.TryGetRandomWaypoint(out ResidentWaypoint waypoint))
        {
            if (!s_warnedNoWaypoint)
            {
                s_warnedNoWaypoint = true;
                Debug.LogWarning("[주민] 씬에 쓸 수 있는 ResidentWaypoint가 없어 주민이 이동하지 않습니다. " +
                    "빈 GameObject에 ResidentWaypoint를 붙여 배치하세요. (이 경고는 1회만 표시됩니다)");
            }

            return Status.Failure;
        }

        if (!waypoint.TryGetRandomPoint(out Vector3 point))
        {
            // 조용히 실패한다 — 반경이 NavMesh 밖에 걸친 웨이포인트에서는 정상적으로 자주 일어나고,
            // 다음 주기에 다른 웨이포인트가 뽑히면 해소된다.
            return Status.Failure;
        }

        Destination.Value = point;
        HasDestination.Value = true;

        return Status.Success;
    }
}
