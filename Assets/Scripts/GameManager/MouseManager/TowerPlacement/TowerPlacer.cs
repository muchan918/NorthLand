using System.Collections.Generic;
using UnityEngine;

/// 배치에 필요한 타워 데이터(풋프린트·사거리)의 최소 단위.
/// TowerPlacer는 이 구조체에만 의존하고 특정 SO(TowerAsset/Combat TowerData)에 묶이지 않는다.
/// → 데이터 출처를 자유롭게 교체 가능. 지금은 더미로 채우고, 나중에 SO에서 읽어 "주입"만 하면 된다.
public readonly struct TowerPlacementData
{
    public readonly int GridWidth;    // 풋프린트 가로(셀 수)
    public readonly int GridHeight;   // 풋프린트 세로(셀 수)
    public readonly float AttackRange; // 사거리(월드 반경) — 미리보기 원 반경

    public TowerPlacementData(int gridWidth, int gridHeight, float attackRange)
    {
        GridWidth = Mathf.Max(1, gridWidth);
        GridHeight = Mathf.Max(1, gridHeight);
        AttackRange = attackRange;
    }
}

/// 타워 배치 어댑터: 전투 공간 그리드의 허용 셀(건설 가능 타일)에 W×H 풋프린트로 타워를 배치한다.
/// MouseManager의 배치 흐름(PlacementRequest)을 재사용하며, 타일 종류·점유는 BattleTile로 판정한다.
/// 자원 차감·낮 전용 게이팅은 훅 지점만 표시(TODO) — Docs/Core/TowerPlacement.md §8.
public class TowerPlacer : MonoBehaviour
{
    [Header("배치물")]
    [SerializeField] private GameObject towerPrefab; // 실제 배치될 타워(Combat)
    [SerializeField] private GameObject ghostPrefab; // 고스트(Collider 없음)
    [SerializeField] private bool keepPlacing;       // 연속 배치 여부

    [Header("그리드")]
    [Tooltip("셀 간격. StageBuilder.TileSize(=5)와 일치해야 풋프린트 셀 조회가 맞는다.")]
    [SerializeField] private float tileSize = 5f;

    // ── 더미 타워 데이터 (실데이터는 #25에서 주입 — BeginTowerPlacement(data) 참고) ──────────
    // #13은 "더미로 시작" 경로다. 아래 값은 임시이며 실제 값은 muchan TowerAsset/CSV(#25)에서 온다.
    // 실데이터 연결 시 이 파일을 고칠 필요 없이, 주입 오버로드로 TowerPlacementData만 넘기면 된다.
    [Header("더미 타워 데이터 (임시 — #25에서 SO 주입으로 교체)")]
    [SerializeField] private int dummyGridWidth = 1;
    [SerializeField] private int dummyGridHeight = 1;
    [SerializeField] private float dummyAttackRange = 3f;

    [Header("사거리 미리보기")]
    [SerializeField] private Color rangeColor = new Color(0.2f, 0.8f, 1f, 0.9f);
    [SerializeField] private int rangeSegments = 48;

    private GameObject _rangeIndicator;
    private TowerPlacementData _activeData; // 현재 배치 중인 타워의 데이터(주입 또는 더미)

    // ── 진입점 ─────────────────────────────────────────────────────────────────────
    /// 더미 데이터로 배치 시작. UI 버튼 OnClick에 연결하는 파라미터 없는 오버로드(테스트/임시용).
    public void BeginTowerPlacement()
    {
        BeginTowerPlacement(new TowerPlacementData(dummyGridWidth, dummyGridHeight, dummyAttackRange));
    }

    /// 실데이터 "주입" 경로. 나중에 #25/#71에서 TowerAsset(SO)을 읽어 이 오버로드로 넘기면,
    /// 배치·검증·미리보기 로직은 무수정으로 실데이터에 그대로 동작한다. 예시 매핑:
    ///
    ///     var d = new TowerPlacementData(
    ///         asset.Data.GridWidth,              // CSV 풋프린트(muchan TowerData)
    ///         asset.Data.GridHeight,
    ///         asset.Single.Attack.AttackRange);  // TowerType에 맞는 그룹의 AttackRange
    ///     towerPlacer.BeginTowerPlacement(d);
    ///
    /// (TowerPlacer는 SO 타입을 알 필요가 없다 — 매핑은 호출부가 담당해 결합을 끊는다.)
    public void BeginTowerPlacement(TowerPlacementData data)
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

        _activeData = data;

        MouseManager.Instance.BeginPlacement(new PlacementRequest
        {
            GhostPrefab = ghostPrefab,
            Snap = SnapToFootprintCenter,
            CanPlaceAt = CanPlaceFootprint,
            OnConfirmed = PlaceTower,
            OnEnded = EndPlacement,
            KeepPlacingAfterConfirm = keepPlacing,
        });

        // 사거리 원은 BeginPlacement 이후에 생성한다.
        // (BeginPlacement 내부의 CancelPlacement가 이전 배치의 OnEnded=EndPlacement를 먼저 발화해
        //  방금 만든 원을 지우는 순서 문제를 피하기 위함)
        CreateRangeIndicator(_activeData.AttackRange);
    }

    // ── 스냅: 앵커(히트 타일) 기준 W×H 풋프린트의 중심 월드 좌표 ─────────────────────
    // 그리드가 월드 X/Z축에 정렬돼 있다고 가정한다(battlespace 회전 없음). 사거리 원도 여기로 이동.
    private Vector3 SnapToFootprintCenter(RaycastHit hit)
    {
        Vector3 result;
        BattleTile anchor = hit.collider.GetComponentInParent<BattleTile>();
        if (anchor != null)
        {
            Vector3 a = anchor.transform.position;
            result = new Vector3(
                a.x + (_activeData.GridWidth - 1) * 0.5f * tileSize,
                hit.point.y,
                a.z + (_activeData.GridHeight - 1) * 0.5f * tileSize);
        }
        else
        {
            result = hit.point;
        }

        if (_rangeIndicator != null) _rangeIndicator.transform.position = result;
        return result;
    }

    // ── 유효성: 풋프린트 전 셀이 건설 가능(Grass) & 미점유여야 함 (Docs §4) ────────────
    private bool CanPlaceFootprint(RaycastHit hit)
    {
        BattleTile anchor = hit.collider.GetComponentInParent<BattleTile>();
        if (anchor == null) return false;

        foreach (BattleTile tile in FootprintTiles(anchor))
        {
            if (tile == null || tile.Kind != TileKind.Grass || tile.Occupied) return false;
        }

        // TODO(훅): 자원 충분 여부(ResourceWallet) / 낮 페이즈 여부 — Docs/Core/TowerPlacement.md §8
        return true;
    }

    // ── 확정: 풋프린트 전 셀 점유 + 중심에 타워 생성 ──────────────────────────────────
    private void PlaceTower(RaycastHit hit, Vector3 snappedPos)
    {
        BattleTile anchor = hit.collider.GetComponentInParent<BattleTile>();
        if (anchor == null) return;

        // 방어적 재확인 후 점유 대상 수집(하나라도 무효면 중단)
        var footprint = new List<BattleTile>();
        foreach (BattleTile tile in FootprintTiles(anchor))
        {
            if (tile == null || tile.Kind != TileKind.Grass || tile.Occupied) return;
            footprint.Add(tile);
        }

        // TODO(훅): ResourceWallet.TrySpend(cost) 실패 시 return — Docs/Core/TowerPlacement.md §8

        Instantiate(towerPrefab, snappedPos, Quaternion.identity);
        foreach (BattleTile tile in footprint) tile.Occupied = true;
    }

    // 앵커 기준 W×H 각 셀 위치의 BattleTile을 공간 질의로 수집(없으면 null 포함해 yield).
    // 셀→타일 레지스트리가 없으므로 좌표(tileSize 간격)로 그 지점을 OverlapSphere해서 찾는다.
    private IEnumerable<BattleTile> FootprintTiles(BattleTile anchor)
    {
        Vector3 a = anchor.transform.position;
        for (int i = 0; i < _activeData.GridWidth; i++)
        {
            for (int j = 0; j < _activeData.GridHeight; j++)
            {
                Vector3 cell = new Vector3(a.x + i * tileSize, a.y, a.z + j * tileSize);
                yield return TileAt(cell);
            }
        }
    }

    private BattleTile TileAt(Vector3 worldCell)
    {
        // 반경은 셀 간격의 절반 미만이어야 인접 셀을 잘못 잡지 않는다.
        Collider[] hits = Physics.OverlapSphere(worldCell, tileSize * 0.4f);
        foreach (Collider c in hits)
        {
            BattleTile tile = c.GetComponentInParent<BattleTile>();
            if (tile != null) return tile;
        }
        return null;
    }

    // ── 사거리 미리보기(런타임 LineRenderer 원) ──────────────────────────────────────
    private void CreateRangeIndicator(float range)
    {
        if (_rangeIndicator != null) Destroy(_rangeIndicator);

        _rangeIndicator = new GameObject("TowerRangePreview");
        LineRenderer lr = _rangeIndicator.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; // 원을 로컬로 그리고 오브젝트를 이동시켜 중심 추적
        lr.loop = true;
        lr.widthMultiplier = 0.15f;
        lr.material = new Material(Shader.Find("Sprites/Default")); // 언릿 컬러 라인(URP에서 동작)
        lr.startColor = lr.endColor = rangeColor;

        lr.positionCount = rangeSegments;
        for (int i = 0; i < rangeSegments; i++)
        {
            float angle = (i / (float)rangeSegments) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * range, 0.05f, Mathf.Sin(angle) * range));
        }
    }

    // 배치 종료(취소/확정 복귀) 시 프리뷰 정리. PlacementRequest.OnEnded로 연결됨.
    private void EndPlacement()
    {
        if (_rangeIndicator != null) Destroy(_rangeIndicator);
        _rangeIndicator = null;
    }
}
