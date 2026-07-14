using UnityEngine;

/// 타워 배치 어댑터: 전투 공간 그리드의 허용 셀(건설 가능 타일)에만 타워를 배치한다.
/// MouseManager의 배치 흐름(PlacementRequest)을 재사용하며, 타일 종류·점유는 BattleTile로 판정한다.
/// 자원 차감·낮 전용 게이팅은 훅 지점만 표시(TODO) — Docs/Core/TowerPlacement.md §8.
public class TowerPlacer : MonoBehaviour
{
    [SerializeField] private GameObject towerPrefab; // 실제 배치될 타워(Combat)
    [SerializeField] private GameObject ghostPrefab; // 고스트(Collider 없음)
    [SerializeField] private bool keepPlacing;       // 연속 배치 여부

    /// UI 버튼 OnClick 등에서 호출 → 타워 배치 모드 시작.
    public void BeginTowerPlacement()
    {
        if (MouseManager.Instance == null)
        {
            Debug.LogError("[TowerPlacer] MouseManager가 씬에 없습니다.");
            return;
        }

        if (ghostPrefab == null || towerPrefab == null)
        {
            Debug.LogError("[TowerPlacer] ghostPrefab/towerPrefab을 인스펙터에서 지정하세요.");
            return;
        }

        MouseManager.Instance.BeginPlacement(new PlacementRequest
        {
            GhostPrefab = ghostPrefab,
            Snap = SnapToTileCenter,
            CanPlaceAt = CanPlaceOnTile,
            OnConfirmed = PlaceTower,
            KeepPlacingAfterConfirm = keepPlacing,
        });
    }

    // 고스트를 커서 밑 타일 중심으로 스냅(수평). 높이는 히트 표면을 유지한다.
    private Vector3 SnapToTileCenter(RaycastHit hit)
    {
        BattleTile tile = hit.collider.GetComponentInParent<BattleTile>();
        if (tile == null) return hit.point;

        Vector3 center = tile.transform.position;
        return new Vector3(center.x, hit.point.y, center.z);
    }

    // 허용 셀 = 건설 가능(Grass) 타일 & 미점유. (Docs/Core/TowerPlacement.md §4)
    private bool CanPlaceOnTile(RaycastHit hit)
    {
        BattleTile tile = hit.collider.GetComponentInParent<BattleTile>();
        if (tile == null) return false;
        if (tile.Kind != TileKind.Grass) return false;
        if (tile.Occupied) return false;

        // TODO(훅): 자원 충분 여부 / 낮 페이즈 여부 — Docs/Core/TowerPlacement.md §8
        return true;
    }

    // 확정: 타일 중심에 타워 생성 + 점유 표시. 상태를 한 번 더 확인(방어).
    private void PlaceTower(RaycastHit hit, Vector3 snappedPos)
    {
        BattleTile tile = hit.collider.GetComponentInParent<BattleTile>();
        if (tile == null || tile.Kind != TileKind.Grass || tile.Occupied) return;

        if (towerPrefab == null)
        {
            Debug.LogError("[TowerPlacer] towerPrefab이 지정되지 않았습니다.");
            return;
        }

        // TODO(훅): ResourceWallet.TrySpend(cost) 실패 시 return — Docs/Core/TowerPlacement.md §8

        Instantiate(towerPrefab, snappedPos, Quaternion.identity);
        tile.Occupied = true;
    }
}
