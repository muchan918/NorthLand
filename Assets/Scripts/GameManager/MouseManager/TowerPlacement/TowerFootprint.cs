using System.Collections.Generic;
using UnityEngine;

/// 배치된 타워가 점유한 전투 타일을 기록하고, 파괴 시 점유를 해제한다.
/// TowerPlacer가 배치 확정 시 인스턴스에 부착한다. 합성 재료 소모(#195)·향후 철거 등
/// 어떤 이유로든 타워가 Destroy되면 OnDestroy에서 그 타일들의 Occupied를 false로 되돌려
/// 그 자리에 재배치를 허용한다.
public class TowerFootprint : MonoBehaviour
{
    private readonly List<BattleTile> _tiles = new List<BattleTile>();
    public IReadOnlyList<BattleTile> Tiles =>_tiles;

    /// 점유할 타일을 등록하고 즉시 Occupied=true로 잠근다.
    public void Occupy(BattleTile tile)
    {
        if (tile == null || _tiles.Contains(tile)) return;
        tile.Occupied = true;
        _tiles.Add(tile);
    }

    private void OnDestroy()
    {
        foreach (BattleTile tile in _tiles)
        {
            if (tile != null) tile.Occupied = false;
        }
    }
}
