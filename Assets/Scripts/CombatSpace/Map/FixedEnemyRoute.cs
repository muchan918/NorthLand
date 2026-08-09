using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    public sealed class FixedEnemyRoute : MonoBehaviour
    {
        [Header("Route")]
        [SerializeField]
        private List<Transform> waypoints = new List<Transform>();

        [SerializeField]
        private Transform coordinateRoot;

        [SerializeField]
        private List<Vector3> localWaypoints = new List<Vector3>();

        [Header("Validation")]
        [SerializeField]
        [Min(0.01f)]
        private float minimumWaypointDistance = 0.1f;

        public IReadOnlyList<Transform> Waypoints => waypoints;

        public int WaypointCount => waypoints.Count;

        public bool TryGetWorldPoints(List<Vector3> result)
        {
            if (result == null)
            {
                Debug.LogError("[FixedEnemyRoute] 결과 목록이 null입니다.",this);

                return false;
            }

            result.Clear();

            if (!ValidateRoute())
            {
                return false;
            }

            if (coordinateRoot == null)
            {
                Debug.LogError("[FixedEnemyRoute] Coordinate Root가 지정되지 않았습니다.",this);

                return false;
            }

            if (localWaypoints == null ||localWaypoints.Count != waypoints.Count)
            {
                Debug.LogError("[FixedEnemyRoute] 저장된 로컬 경로가 없거나 웨이포인트 개수와 일치하지 않습니다. 경로를 다시 Bake하세요.",this);

                return false;
            }

            foreach (Vector3 localPoint in localWaypoints)
            {
                result.Add(coordinateRoot.TransformPoint(localPoint));
            }

            return true;
        }

        public bool ValidateRoute()
        {
            if (waypoints == null || waypoints.Count < 2)
            {
                Debug.LogError("[FixedEnemyRoute] 웨이포인트를 2개 이상 등록해야 합니다.",this);

                return false;
            }

            for (int i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i] == null)
                {
                    Debug.LogError(
                        $"[FixedEnemyRoute] Waypoints의 {i}번 항목이 비어 있습니다.",
                        this);

                    return false;
                }

                if (i == 0)
                {
                    continue;
                }

                float distance = Vector3.Distance(
                    waypoints[i - 1].position,
                    waypoints[i].position);

                if (distance < minimumWaypointDistance)
                {
                    Debug.LogError($"[FixedEnemyRoute] {i - 1}번과 {i}번 웨이포인트가 같은 위치에 있거나 너무 가깝습니다.",this);

                    return false;
                }
            }

            return true;
        }

        private void OnDrawGizmos()
        {
            if (coordinateRoot == null ||localWaypoints == null ||localWaypoints.Count == 0)
            {
                return;
            }

            for (int i = 0; i < localWaypoints.Count; i++)
            {
                Vector3 worldPoint = coordinateRoot.TransformPoint(localWaypoints[i]);

                if (i == 0)
                    Gizmos.color = Color.green;
                else if (i == localWaypoints.Count - 1)
                    Gizmos.color = Color.yellow;
                else
                    Gizmos.color = Color.cyan;

                Gizmos.DrawSphere(worldPoint, 0.5f);

                if (i == 0)
                    continue;

                Vector3 previousWorldPoint = coordinateRoot.TransformPoint(localWaypoints[i - 1]);

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(previousWorldPoint, worldPoint);
            }
        }

        [ContextMenu("Bake Waypoints To Coordinate Root")]
        private void BakeWaypointsToCoordinateRoot()
        {
            if (coordinateRoot == null)
            {
                Debug.LogError("[FixedEnemyRoute] Coordinate Root가 지정되지 않았습니다.",this);

                return;
            }

            if (waypoints == null || waypoints.Count < 2)
            {
                Debug.LogError("[FixedEnemyRoute] 웨이포인트를 2개 이상 등록해야 합니다.",this);

                return;
            }

            localWaypoints.Clear();

            for (int i = 0; i < waypoints.Count; i++)
            {
                Transform waypoint = waypoints[i];

                if (waypoint == null)
                {
                    Debug.LogError($"[FixedEnemyRoute] {i}번 웨이포인트가 비어 있습니다.",this);

                    localWaypoints.Clear();
                    return;
                }

                localWaypoints.Add(coordinateRoot.InverseTransformPoint(waypoint.position));
            }

            Debug.Log($"[FixedEnemyRoute] {localWaypoints.Count}개 웨이포인트를{coordinateRoot.name} 기준으로 저장했습니다.",this);
        }

    }
}
