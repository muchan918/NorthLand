using UnityEngine;

namespace NorthLand.Core
{
    public sealed partial class RunSaveManager
    {
        /// <summary>
        /// 현재 확보한 영토 노드 ID를 수집한다.
        /// 맵 구조와 영토 효과 수치는 시드로 다시 생성하므로 저장하지 않는다.
        /// </summary>
        /// <param name="data">
        /// 수집된 영토 상태. 실패하면 null.
        /// </param>
        /// <returns>
        /// 영토 그래프가 준비되어 있고 수집에 성공하면 true.
        /// </returns>
        private bool TryCaptureTerritory(out TerritorySaveData data)
        {
            data = null;

            if (territory == null)
            {
                Debug.LogError("[Save] TerritoryController가 연결되지 않았습니다.",this);

                return false;
            }

            TerritoryGraph graph = territory.Graph;

            if (graph == null)
            {
                Debug.LogError("[Save] 영토 그래프가 준비되지 않았습니다.",this);

                return false;
            }

            data = new TerritorySaveData();

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                TerritoryNode node = graph.Nodes[i];

                if (node == null)
                {
                    Debug.LogError($"[Save] 영토 노드 {i}번 항목이 비어 있습니다.",this);

                    data = null;
                    return false;
                }

                if (graph.IsOwned(node.Id))
                {
                    data.OwnedNodeIds.Add(node.Id);
                }
            }

            return true;
        }

        /// <summary>
        /// 저장된 확보 노드 목록을 현재 영토 그래프에 복원한다.
        /// 영토 그래프는 저장된 시드로 먼저 생성되어 있어야 한다.
        /// </summary>
        /// <param name="data">복원할 영토 상태.</param>
        /// <returns>영토 상태 복원에 성공하면 true.</returns>
        private bool TryRestoreTerritory(TerritorySaveData data)
        {
            if (territory == null)
            {
                Debug.LogError("[Load] TerritoryController가 연결되지 않았습니다.",this);

                return false;
            }

            if (data == null)
            {
                Debug.LogError("[Load] 영토 세이브 데이터가 없습니다.",this);

                return false;
            }

            if (data.OwnedNodeIds == null)
            {
                Debug.LogError("[Load] 확보 영토 노드 목록이 없습니다.",this);

                return false;
            }

            TerritoryGraph graph = territory.Graph;

            if (graph == null)
            {
                Debug.LogError("[Load] 영토 그래프가 준비되지 않았습니다.",this);

                return false;
            }

            if (!graph.TryRestoreOwnedNodes(data.OwnedNodeIds))
            {
                Debug.LogError("[Load] 영토 상태 복원에 실패했습니다.",this);

                return false;
            }

            return true;
        }
    }
}

