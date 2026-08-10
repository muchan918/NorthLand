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


    private BattleTile _anchorTile;
    private Vector2Int _anchorCell;
    private bool _hasAnchorCell;

    /// <summary>타워를 배치할 때 기준으로 사용한 실제 타일.</summary>
    public BattleTile AnchorTile => _anchorTile;

    /// <summary>기준 타일이 정상적으로 기록됐는가.</summary>
    public bool HasAnchorTile => _anchorTile != null;


    /// <summary>타워 풋프린트의 좌측 아래 기준 셀 좌표.</summary>
    public Vector2Int AnchorCell => _anchorCell;

    /// <summary>기준 셀 좌표가 정상적으로 기록됐는가.</summary>
    public bool HasAnchorCell => _hasAnchorCell;

    /// <summary>
    /// 타워 배치 시 사용한 기준 타일의 논리 셀 좌표를 기록한다.
    /// </summary>
    public void SetAnchorCell(Vector2Int cell)
    {
        _anchorCell = cell;
        _hasAnchorCell = true;
    }


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

        for (int i = _tiles.Count - 1; i >= 0; i--)
        {
            BattleTile tile = _tiles[i];
            if (tile == null) continue;

            // 비워둔 사이에 다른 타워가 자리를 차지한 경우. 되찾으면 겹쳐 서는 것으로 끝나지 않는다 —
            // **소유권까지 가져와, 내가 파괴될 때 아래 OnDestroy가 남의 타일을 풀어버린다.** 빗장이
            // 막으려던 바로 그 증상이 되살아나는 자리다. 그래서 되찾지 않고 목록에서 뺀다.
            if (tile.Occupied)
            {
                Debug.LogWarning(
                    $"[TowerFootprint] {name}: 되돌리려는 타일이 이미 점유돼 있습니다 — " +
                    "타일 소유권은 실제 점유자에게 남기고 목록에서 제외합니다.", this);
                _tiles.RemoveAt(i);
                continue;
            }

            tile.Occupied = true;
        }
    }

    private void OnEnable()
    {
        // 안전망: Release된 채로 되살아난 경우 점유를 되돌린다.
        // 정상 경로(`TowerMergeCommand.Undo`)는 활성화 **전에** Reoccupy를 부르므로 여기 도달할 땐
        // 이미 `_released == false`라 no-op이고, 배치 시 AddComponent되는 순간도 마찬가지다.
        // 커맨드를 거치지 않고 `SetActive(true)`가 걸리는 경로(향후 연출·철거 등)만 자기치유한다 —
        // 그 경우 타워는 살아나는데 타일은 빈 칸으로 남아 그 위에 또 배치되고, 증상이 원인에서 멀다.
        // 컴포넌트 간 OnEnable 순서는 미정의이므로 이건 어디까지나 안전망이고,
        // "타일을 먼저 되돌린 뒤 살린다"는 Undo의 명시적 순서가 계약이다.
        if (_released) Reoccupy();
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

    /// <summary>타워 배치 시 사용한 실제 기준 타일을 기록한다.</summary>
    public void SetAnchorTile(BattleTile tile)
    {
        _anchorTile = tile;
    }
}
