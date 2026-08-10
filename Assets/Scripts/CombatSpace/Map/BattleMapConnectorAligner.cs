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

        [Header("Connectors")]
        [SerializeField]
        private Transform startMapEndConnector;

        [SerializeField]
        private Transform battleMapStartConnector;

        public bool TryAlign()
        {
            if (mapGenerator == null ||tileSpawner == null ||generatedBattleMapRoot == null ||startMapEndConnector == null ||battleMapStartConnector == null)
            {
                Debug.LogError(
                    "[BattleMapConnectorAligner] 필수 참조가 지정되지 않았습니다.",
                    this);

                return false;
            }

            CombatMapData map = mapGenerator.CurrentMap;

            if (map == null ||map.EnemyRoute == null ||map.EnemyRoute.Count == 0)
            {
                Debug.LogError(
                    "[BattleMapConnectorAligner] 자동 생성 경로가 없습니다.",
                    this);

                return false;
            }

            // 현재 자동 경로의 첫 지점을 배틀맵 Connector에 기록한다.
            Vector3 generatedRouteStart = tileSpawner.GridToWorldPosition(map.EnemyRoute[0]);

            battleMapStartConnector.position = generatedRouteStart;

            // XZ 위치만 스타트맵 끝 Connector에 맞춘다.
            // 높이는 기존 경로 결합 로직에서 정렬한다.
            Vector3 positionDelta =
                startMapEndConnector.position - battleMapStartConnector.position;

            positionDelta.y = 0f;
            generatedBattleMapRoot.position += positionDelta;

            Debug.Log(
                $"[BattleMapConnectorAligner] 배틀맵 정렬 완료. 이동량: {positionDelta}",
                this);

            return true;
        }
    }
}
