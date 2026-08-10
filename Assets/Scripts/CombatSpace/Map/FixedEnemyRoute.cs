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

            foreach (Transform waypoint in waypoints)
            {
                result.Add(waypoint.position);
            }

            return true;
        }

        public bool ValidateRoute()
        {
            if (coordinateRoot == null)
            {
                Debug.LogError(
                    "[FixedEnemyRoute] Coordinate Root가 지정되지 않았습니다.",
                    this);

                return false;
            }

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

                if (!waypoints[i].IsChildOf(coordinateRoot))
                {
                    Debug.LogError(
                        $"[FixedEnemyRoute] {i}번 웨이포인트가 " +
                        $"{coordinateRoot.name} 하위에 없습니다.",
                        waypoints[i]);

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
            if (waypoints == null || waypoints.Count == 0)
            {
                return;
            }

            for (int i = 0; i < waypoints.Count; i++)
            {
                Transform waypoint = waypoints[i];

                if (waypoint == null)
                {
                    continue;
                }

                if (i == 0)
                    Gizmos.color = Color.green;
                else if (i == waypoints.Count - 1)
                    Gizmos.color = Color.yellow;
                else
                    Gizmos.color = Color.cyan;

                Gizmos.DrawSphere(waypoint.position, 0.5f);

                if (i == 0 || waypoints[i - 1] == null)
                    continue;

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(
                    waypoints[i - 1].position,
                    waypoint.position);
            }
        }
    }
}
