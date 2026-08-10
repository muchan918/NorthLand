using UnityEngine;

namespace CombatSpace
{
    public sealed class BattleMapConnectorAligner : MonoBehaviour
    {
        [Header("Map")]
        [SerializeField]
        private CombatMapGenerator mapGenerator;

        [SerializeField]
        private CombatMapTileSpawner tileSpawner;

        [SerializeField]
        private Transform generatedBattleMapRoot;

        [SerializeField]
        private FixedEnemyRoute fixedEnemyRoute;

        [SerializeField]
        private Transform battleMapStartConnector;

        public bool TryAlign()
        {
            if (mapGenerator == null ||tileSpawner == null ||generatedBattleMapRoot == null ||battleMapStartConnector == null)
            {
                Debug.LogError("[BattleMapConnectorAligner] 필수 참조가 지정되지 않았습니다.",this);

                return false;
            }

            if (fixedEnemyRoute == null ||fixedEnemyRoute.Waypoints == null ||fixedEnemyRoute.Waypoints.Count == 0)
            {
                Debug.LogError("[BattleMapConnectorAligner] 고정 경로의 끝점을 찾을 수 없습니다.",this);

                return false;
            }

            Transform startMapEndConnector = fixedEnemyRoute.Waypoints[fixedEnemyRoute.Waypoints.Count - 1];

            if (startMapEndConnector == null)
            {
                Debug.LogError("[BattleMapConnectorAligner] 고정 경로의 마지막 웨이포인트가 비어 있습니다.",this);

                return false;
            }

            var map = mapGenerator.CurrentMap;

            if (map == null ||map.EnemyRoute == null ||map.EnemyRoute.Count == 0)
            {
                Debug.LogError("[BattleMapConnectorAligner] 자동 생성된 적 이동 경로가 없습니다.",this);

                return false;
            }

            // 자동 경로의 첫 지점을 배틀맵 Connector에 기록한다.
            Vector3 generatedRouteStart = tileSpawner.GridToWorldPosition(map.EnemyRoute[0]);

            battleMapStartConnector.position = generatedRouteStart;

            // XZ 위치만 스타트맵의 마지막 웨이포인트에 맞춘다.
            // Y 높이는 경로 결합 과정에서 보정한다.
            Vector3 positionDelta = startMapEndConnector.position - battleMapStartConnector.position;

            positionDelta.y = 0f;

            generatedBattleMapRoot.position += positionDelta;

            Debug.Log($"[BattleMapConnectorAligner] 배틀맵 정렬 완료. 이동량: {positionDelta}",this);

            return true;
        }
    }
}
