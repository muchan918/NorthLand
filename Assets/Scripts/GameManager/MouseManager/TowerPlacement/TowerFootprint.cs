using System.Collections.Generic;
using UnityEngine;

/// 배치된 타워가 점유한 전투 타일을 기록하고, 파괴 시 점유를 해제한다.
/// TowerPlacer가 배치 확정 시 인스턴스에 부착한다. 합성 재료 소모(#195)·향후 철거 등
/// 어떤 이유로든 타워가 Destroy되면 OnDestroy에서 그 타일들의 Occupied를 false로 되돌려
/// 그 자리에 재배치를 허용한다.
///
/// 합성 커맨드(#263)는 파괴 대신 Release/Reoccupy로 점유만 임시로 푼다 — 후보 버튼을 누르는 즉시
/// 재료 자리를 비워 그 위에 결과 타워를 놓을 수 있게 하되, 배치를 취소하면 원복하기 위함이다.
public class TowerFootprint : MonoBehaviour
{
    private readonly List<BattleTile> _tiles = new List<BattleTile>();
    public IReadOnlyList<BattleTile> Tiles =>_tiles;

    // 점유를 놓아준 상태인가(Release 이후 Reoccupy 전). OnDestroy가 남의 점유를 건드리지 않게 하는 빗장이다.
    private bool _released;

    /// 점유할 타일을 등록하고 즉시 Occupied=true로 잠근다.
    public void Occupy(BattleTile tile)
    {
        if (tile == null || _tiles.Contains(tile)) return;
        tile.Occupied = true;
        _tiles.Add(tile);
    }

    /// 등록 목록은 유지한 채 점유만 푼다(합성 소프트 소모 #263). 되돌리려면 Reoccupy.
    public void Release()
    {
        if (_released) return;
        _released = true;

        foreach (BattleTile tile in _tiles)
        {
            if (tile != null) tile.Occupied = false;
        }
    }

    /// Release로 풀어둔 점유를 되돌린다(합성 배치 취소 #263).
    public void Reoccupy()
    {
        if (!_released) return;
        _released = false;

        foreach (BattleTile tile in _tiles)
        {
            if (tile == null) continue;

            // 비워둔 사이에 다른 타워가 자리를 차지했다면 여기서 겹쳐 서게 된다. 배치 세션이 한 번에
            // 하나뿐이고 연속 배치가 꺼져 있어 구조상 도달하지 않지만, 조용히 겹치는 것보다는 남긴다.
            if (tile.Occupied)
            {
                Debug.LogWarning($"[TowerFootprint] {name}: 되돌리려는 타일이 이미 점유돼 있습니다 — 겹쳐 섭니다.", this);
            }
            tile.Occupied = true;
        }
    }

    private void OnDestroy()
    {
        // Release로 점유를 이미 놓아준 뒤라면 그 타일은 더 이상 내 것이 아니다. 그 사이에 다른 타워가
        // 들어와 점유했을 수 있고 — 합성이 재료가 있던 자리에 결과를 놓는 경로가 정확히 이것이다 —
        // 여기서 무조건 false로 밀면 **남이 서 있는 타일이 빈 칸으로 표시돼 그 위에 또 배치된다.**
        if (_released) return;

        foreach (BattleTile tile in _tiles)
        {
            if (tile != null) tile.Occupied = false;
        }
    }
}
