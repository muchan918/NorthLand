using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Core
{
    /// <summary>
    /// 현재 실행 중인 한 판의 저장 데이터를 수집하고 복원하는 중앙 관리자.
    /// 영역별 구현은 partial 파일로 분리하며 복원 순서는 이 타입이 명시적으로 소유한다.
    /// </summary>
    public sealed partial class RunSaveManager : MonoBehaviour
    {
        [Tooltip("자원·생산 건물·업그레이드 건물·증축 주민 상태를 제공하는 경영 시스템")]
        [SerializeField]
        private ManagementController management;

        [SerializeField]
        private TerritoryController territory;

        [Tooltip("저장된 셀 좌표에 타워를 다시 배치할 때 사용하는 배치 시스템")]
        [SerializeField]
        private TowerPlacer towerPlacer;

        [Tooltip("저장된 TowerID를 실제 TowerAsset으로 변환하는 목록")]
        [SerializeField]
        private List<TowerAsset> towerAssets = new();
    }
}

