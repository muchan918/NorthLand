using System.Collections.Generic;
using UnityEngine;

/// 배치에 필요한 타워 데이터(풋프린트·사거리)의 최소 단위.
/// TowerPlacer는 이 구조체에만 의존하고 특정 SO(TowerAsset/Combat TowerData)에 묶이지 않는다.
/// → 데이터 출처를 자유롭게 교체 가능. 지금은 더미로 채운다.
/// (나중에 tower/ghost 프리팹 + footprint/range를 담은 SO를 게이트웨이로 주입받아 채운다 — TowerPlacer 참고.)
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
    // WL-032: StageBuilder.TileSize와 수동 동기화(단일 출처 아님). 불일치 시 Awake에서 경고한다.
    [Tooltip("셀 간격. StageBuilder.TileSize(=5)와 일치해야 풋프린트 셀 조회가 맞는다.")]
    [SerializeField] private float tileSize = 5f;

    // ── 더미 타워 데이터 (임시 — #13 "더미로 시작" 경로) ─────────────────────────────
    // 나중에 tower/ghost 프리팹 + footprint/range를 담은 SO가 생기면, TowerPlacer가 그 SO를
    // 게이트웨이로 주입받아(BeginTowerPlacement의 예정 오버로드) 위 프리팹과 아래 더미 값을 대체한다.
    [Header("더미 타워 데이터 (임시 — 나중에 SO에서 주입)")]
    [SerializeField] private int dummyGridWidth = 1;
    [SerializeField] private int dummyGridHeight = 1;
    [SerializeField] private float dummyAttackRange = 3f;

    [Header("사거리 미리보기")]
    [SerializeField] private Color rangeColor = new Color(0.2f, 0.8f, 1f, 0.9f);
    [SerializeField] private int rangeSegments = 48;

    [Header("셀 하이라이트")]
    [SerializeField] private Color validCellColor = new Color(0.2f, 1f, 0.3f, 0.35f);
    [SerializeField] private Color invalidCellColor = new Color(1f, 0.25f, 0.2f, 0.35f);

    private GameObject _rangeIndicator;
    private readonly List<GameObject> _cellHighlights = new List<GameObject>();
    private Material _rangeMat;
    private Material _cellMatValid;
    private Material _cellMatInvalid;
    private TowerPlacementData _activeData; // 현재 배치 중인 타워의 데이터(주입 또는 더미)
    private IReadOnlyList<ResourceCost> _activeCost; // 현재 배치 중인 타워의 비용(확정 시 차감)
    private System.Action _onConfirmed; // 배치 확정 후 1회 콜백(합성 재료 소모 등). 확정 직후 소비하고 비운다.
    private ManagementController _management; // 자원 차감 게이트웨이(WL-017). null이면 무료 배치(테스트 씬).

    // 프레임당 풋프린트를 1회만 계산해 캐시(스냅에서 채우고, 검증·하이라이트가 공유).
    // MouseManager는 매 프레임 Snap → CanPlaceAt 순으로 호출하므로 CanPlaceAt은 이 캐시를 신뢰한다.
    private readonly List<(Vector3 pos, BattleTile tile)> _footprint = new List<(Vector3, BattleTile)>();
    // OverlapSphere 재사용 버퍼(배치 중 매 프레임 힙 할당 방지). 셀 하나에 겹치는 콜라이더는 소수.
    private readonly Collider[] _overlap = new Collider[8];

    private void Awake()
    {
        // 타일 간격 단일 출처화(WL-034 완화): 신맵 CombatMapGenerator.Settings.TileSize가 있으면 그 값을 쓴다.
        // 인스펙터 tileSize는 폴백(구맵/테스트 씬). 신맵 타일은 15인데 tileSize=5면 하이라이트 쿼드가
        // 타일보다 훨씬 작게(≈1/3) 그려져 타워 고스트에 가리고, 다중 셀 풋프린트도 어긋난다.
        var combatMap = FindFirstObjectByType<CombatSpace.CombatMapGenerator>();
        if (combatMap != null && combatMap.Settings != null && combatMap.Settings.TileSize > 0f)
        {
            tileSize = combatMap.Settings.TileSize;
        }

        // WL-032/034 방어: 신맵 반영 후에도 구맵(StageBuilder)과 불일치하면 경고한다(둘 다 있는 씬 대비).
        StageBuilder stage = FindFirstObjectByType<StageBuilder>();
        if (stage != null && !Mathf.Approximately(stage.TileSize, tileSize))
        {
            Debug.LogWarning(
                $"[TowerPlacer] tileSize({tileSize})가 StageBuilder.TileSize({stage.TileSize})와 다릅니다. " +
                "풋프린트 셀 조회가 어긋날 수 있습니다.");
        }

        // 자원 차감 게이트웨이(경영 시스템). 씬에 없으면 무료 배치 — 경영 없는 테스트 씬 지원.
        _management = FindFirstObjectByType<ManagementController>();
    }

    private void OnDestroy()
    {
        // 런타임 생성물·머티리얼 정리(누수 방지).
        if (_rangeIndicator != null) Destroy(_rangeIndicator);
        ClearCellHighlights();
        if (_rangeMat != null) Destroy(_rangeMat);
        if (_cellMatValid != null) Destroy(_cellMatValid);
        if (_cellMatInvalid != null) Destroy(_cellMatInvalid);
    }

    // ── 진입점 ─────────────────────────────────────────────────────────────────────
    /// 더미 데이터 + 인스펙터 프리팹으로 배치 시작. UI 버튼 OnClick에 연결(현재 테스트 경로).
    /// 비용은 so.Cost, 확정 콜백 없음(일반 타워 배치).
    public void BeginTowerPlacement(TowerAsset so)
        => BeginTowerPlacement(so, so != null ? so.Cost : null, null);

    /// 비용·확정 콜백을 주입하는 오버로드. 합성(#195)이 결과 타워를 결과 코스트(ExtraCost)로 배치하고,
    /// 배치 확정 직후 onConfirmed(재료 소모)를 실행하는 데 쓴다. 배치 코어는 단일인자 경로와 동일.
    public void BeginTowerPlacement(TowerAsset so, IReadOnlyList<ResourceCost> cost, System.Action onConfirmed)
    {
        if (so == null)
        {
            Debug.LogError("[TowerPlacer] null TowerAsset은 배치할 수 없습니다.");
            return;
        }

        // Data는 패널(TowerSelectPanelView)이 SO 주입 시 채운다 — 여기선 방어만.
        if (so.Data == null)
        {
            Debug.LogError($"[TowerPlacer] TowerData 미주입(TowerID={so.TowerID}) — 패널에서 SO 주입 시 Data를 채웠는지 확인하세요.");
            return;
        }

        towerPrefab = so.TowerPrefab;
        ghostPrefab = so.GhostPrefab;
        _activeCost = cost;
        // _onConfirmed은 StartPlacement가 BeginPlacement '이후'에 설정한다 — BeginPlacement 내부의
        // CancelPlacement가 이전 배치의 OnEnded=EndPlacement를 발화해 _onConfirmed을 null로 지우므로,
        // 여기서 미리 대입하면 합성 재료 소모 콜백이 유실된다(무료 합성 버그).

        TowerType type = so.TowerType;
        switch (type)
        {
            case TowerType.Single:
                StartPlacement(new(so.Data.GridWidth, so.Data.GridHeight, so.Single.Attack.AttackRange), onConfirmed);
                break;
            case TowerType.Area:
                StartPlacement(new(so.Data.GridWidth, so.Data.GridHeight, so.Area.Attack.AttackRange), onConfirmed);
                break;
            case TowerType.Chain:
                StartPlacement(new(so.Data.GridWidth, so.Data.GridHeight, so.Chain.Attack.AttackRange), onConfirmed);
                break;
            case TowerType.Magic:
                // 마법 타워는 오라 반경을 사거리 미리보기로 사용(#111 완료기준 #4).
                // 반경 규칙은 TowerAsset.MagicRadius 단일 출처(WL-056) — AuraTower 실효과와 공유.
                StartPlacement(new(so.Data.GridWidth, so.Data.GridHeight, so.MagicRadius), onConfirmed);
                break;
            default:
                Debug.LogError($"[TowerPlacer] 알 수 없는 TowerType={type}입니다.");
                return;
        }
    }

    // 게이트웨이(예정): tower/ghost 프리팹 + footprint/range를 담은 SO가 생기면 아래 오버로드를 추가한다.
    //   public void BeginTowerPlacement(TowerXxxSO so) {
    //       towerPrefab = so.TowerPrefab; ghostPrefab = so.GhostPrefab;                 // SO가 프리팹까지 제공
    //       StartPlacement(new TowerPlacementData(so.GridWidth, so.GridHeight, so.Range));
    //   }
    // 이러면 위 더미 경로를 대체하며, 배치·검증·미리보기 코어(StartPlacement 이하)는 무수정이다.

    // 실제 배치 시작 코어(진입 방식과 무관). 게이트웨이/더미 어느 경로든 이 메서드를 호출한다.
    // onConfirmed(합성 재료 소모 등)는 BeginPlacement 이후에 설정한다(순서 주의 — 아래 참고).
    private void StartPlacement(TowerPlacementData data, System.Action onConfirmed = null)
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

        // 확정 콜백은 반드시 BeginPlacement '이후'에 설정한다 — 위 BeginPlacement 내부의
        // CancelPlacement→이전 배치 EndPlacement가 _onConfirmed을 null로 지우기 때문(프리뷰와 동일한 순서 이슈).
        _onConfirmed = onConfirmed;

        // 프리뷰는 BeginPlacement 이후에 만든다.
        // (BeginPlacement 내부의 CancelPlacement가 이전 배치의 OnEnded=EndPlacement를 먼저 발화해
        //  방금 만든 프리뷰를 지우는 순서 문제를 피하기 위함)
        CreateRangeIndicator(_activeData.AttackRange);
        CreateCellHighlights(_activeData.GridWidth * _activeData.GridHeight);
    }

    // ── 스냅: 앵커(히트 타일) 기준 W×H 풋프린트의 중심 월드 좌표 ─────────────────────
    // 그리드가 월드 X/Z축에 정렬돼 있다고 가정한다(battlespace 회전 없음). 프리뷰도 여기서 갱신.
    // y는 hit.point.y(레이가 타일 옆면에 맞으면 벽면 높이) 대신 타일 앵커 y를 써서 타워가 항상 윗면에 앉는다.
    private Vector3 SnapToFootprintCenter(RaycastHit hit)
    {
        BattleTile anchor = hit.collider.GetComponentInParent<BattleTile>();
        RebuildFootprint(anchor); // 이번 프레임 풋프린트 1회 계산(검증·하이라이트가 공유)

        Vector3 result = anchor != null
            ? new Vector3(
                anchor.transform.position.x + (_activeData.GridWidth - 1) * 0.5f * tileSize,
                anchor.AnchorPosition.y,
                anchor.transform.position.z + (_activeData.GridHeight - 1) * 0.5f * tileSize)
            : hit.point;

        if (_rangeIndicator != null) _rangeIndicator.transform.position = result;
        UpdateCellHighlights();
        return result;
    }

    // ── 유효성: 풋프린트 전 셀이 건설 가능(Grass) & 미점유여야 함 (Docs §4) ────────────
    // Snap이 이번 프레임 _footprint를 채운 뒤 호출된다(MouseManager 호출 순서) → 캐시 신뢰.
    private bool CanPlaceFootprint(RaycastHit hit)
    {
        if (_footprint.Count == 0) return false; // 앵커 없음(맵 밖) 등
        foreach ((Vector3 _, BattleTile tile) in _footprint)
        {
            if (!IsBuildable(tile)) return false;
        }

        // 자원 부족 시 배치 불가(고스트 빨강) — 연속 배치(keepPlacing) 중 소진돼도 즉시 피드백.
        // (낮 페이즈 게이팅은 여전히 §8 후속 훅)
        if (_management != null && !_management.CanAfford(_activeCost)) return false;
        return true;
    }

    // ── 확정: 풋프린트 전 셀 점유 + 중심에 타워 생성 ──────────────────────────────────
    private void PlaceTower(RaycastHit hit, Vector3 snappedPos)
    {
        // 확정은 프레임당 1회 → 캐시에 의존하지 않고 신선하게 재확인(방어).
        BattleTile anchor = hit.collider.GetComponentInParent<BattleTile>();
        if (anchor == null) return;
        RebuildFootprint(anchor);

        foreach ((Vector3 _, BattleTile tile) in _footprint)
        {
            if (!IsBuildable(tile)) return;
        }

        // 자원 차감(경영 게이트웨이 경유 — TowerPlacement.md §8). 성공 시에만 생성·점유한다.
        // 부족하면 배치 취소. 경영이 씬에 없으면(null) 무료 배치(테스트 씬).
        if (_management != null && !_management.TrySpend(_activeCost))
        {
            // CanPlaceFootprint가 이미 자원 부족을 걸러 여기 도달은 드묾(방어) — 조용한 실패 방지.
            Debug.Log("[TowerPlacer] 자원이 부족해 배치를 취소합니다.");
            return;
        }

        // 점유 타일을 인스턴스에 기록해 둔다: 타워가 파괴되면(합성 소모·철거 등)
        // TowerFootprint.OnDestroy가 그 타일들의 Occupied를 되돌려 재배치를 허용한다.
        var placed = Instantiate(towerPrefab, snappedPos, Quaternion.identity);
        var occupant = placed.AddComponent<TowerFootprint>();
        foreach ((Vector3 _, BattleTile tile) in _footprint) occupant.Occupy(tile);

        // 확정 콜백(합성 재료 소모 등)은 배치 성공 후 1회만 실행한다.
        // 먼저 비우고 호출해 연속 배치(keepPlacing)에서도 재실행되지 않게 한다.
        var confirmed = _onConfirmed;
        _onConfirmed = null;
        confirmed?.Invoke();
    }

    private static bool IsBuildable(BattleTile tile)
        => tile != null && tile.Kind == TileKind.Grass && !tile.Occupied;

    // 앵커 기준 W×H 각 셀의 (중심 위치, BattleTile)을 _footprint에 채운다(재사용 버퍼로 할당 없이 질의).
    // 셀→타일 레지스트리가 없으므로 좌표(tileSize 간격)로 그 지점을 OverlapSphere해서 찾는다.
    private void RebuildFootprint(BattleTile anchor)
    {
        _footprint.Clear();
        if (anchor == null) return;

        Vector3 a = anchor.transform.position;
        for (int i = 0; i < _activeData.GridWidth; i++)
        {
            for (int j = 0; j < _activeData.GridHeight; j++)
            {
                Vector3 cell = new Vector3(a.x + i * tileSize, a.y, a.z + j * tileSize);
                _footprint.Add((cell, TileAt(cell)));
            }
        }
    }

    private BattleTile TileAt(Vector3 worldCell)
    {
        // 반경은 셀 간격의 절반 미만이어야 인접 셀을 잘못 잡지 않는다.
        int count = Physics.OverlapSphereNonAlloc(worldCell, tileSize * 0.4f, _overlap);
        for (int i = 0; i < count; i++)
        {
            BattleTile tile = _overlap[i].GetComponentInParent<BattleTile>();
            if (tile != null) return tile;
        }
        return null;
    }

    // ── 사거리 미리보기(런타임 LineRenderer 원) ──────────────────────────────────────
    private void CreateRangeIndicator(float range)
    {
        if (_rangeIndicator != null) Destroy(_rangeIndicator);
        if (_rangeMat == null) _rangeMat = new Material(Shader.Find("Sprites/Default")); // 언릿, 정점색으로 tint

        _rangeIndicator = new GameObject("TowerRangePreview");
        LineRenderer lr = _rangeIndicator.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; // 원을 로컬로 그리고 오브젝트를 이동시켜 중심 추적
        lr.loop = true;
        lr.widthMultiplier = 0.15f;
        lr.sharedMaterial = _rangeMat; // 공유 머티리얼(매 배치 새로 만들지 않음 — 누수 방지)
        lr.startColor = lr.endColor = rangeColor;

        lr.positionCount = rangeSegments;
        for (int i = 0; i < rangeSegments; i++)
        {
            float angle = (i / (float)rangeSegments) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * range, 0.05f, Mathf.Sin(angle) * range));
        }
    }

    // ── 풋프린트 셀 하이라이트(바닥에 눕힌 반투명 쿼드, 셀별 유효/무효 색) ────────────────
    private void CreateCellHighlights(int count)
    {
        ClearCellHighlights();
        if (_cellMatValid == null) _cellMatValid = MakeUnlitColor(validCellColor);
        if (_cellMatInvalid == null) _cellMatInvalid = MakeUnlitColor(invalidCellColor);

        for (int i = 0; i < count; i++)
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "TowerCellHighlight";
            Destroy(q.GetComponent<Collider>()); // 배치 레이캐스트를 방해하지 않도록 콜라이더 제거
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // XZ 바닥에 눕힘
            q.transform.localScale = Vector3.one * (tileSize * 0.9f);
            _cellHighlights.Add(q);
        }
    }

    // _footprint(스냅에서 계산됨)를 그대로 사용해 각 셀 하이라이트를 배치·색칠한다.
    private void UpdateCellHighlights()
    {
        for (int i = 0; i < _footprint.Count && i < _cellHighlights.Count; i++)
        {
            (Vector3 pos, BattleTile tile) = _footprint[i];
            GameObject q = _cellHighlights[i];
            // 하이라이트 표시 y를 타일 윗면(앵커)에 맞춘다 — 타워 배치 y와 일치. 타일 없으면 풋프린트 y 폴백.
            // (탐지용 RebuildFootprint/TileAt은 루트 y 유지 — 앵커가 콜라이더 위쪽일 때 OverlapSphere 놓침 방지)
            float topY = tile != null ? tile.AnchorPosition.y : pos.y;
            q.transform.position = new Vector3(pos.x, topY + 0.03f, pos.z); // z-파이팅 방지 살짝 위로
            q.GetComponent<Renderer>().sharedMaterial = IsBuildable(tile) ? _cellMatValid : _cellMatInvalid;
        }
    }

    private void ClearCellHighlights()
    {
        foreach (GameObject q in _cellHighlights)
        {
            if (q != null) Destroy(q);
        }
        _cellHighlights.Clear();
    }

    private static Material MakeUnlitColor(Color c)
    {
        Material m = new Material(Shader.Find("Sprites/Default")); // 반투명 언릿(알파 반영)
        m.color = c;
        return m;
    }

    // 배치 종료(취소/확정 복귀) 시 프리뷰 정리. PlacementRequest.OnEnded로 연결됨.
    private void EndPlacement()
    {
        if (_rangeIndicator != null) Destroy(_rangeIndicator);
        _rangeIndicator = null;
        ClearCellHighlights();
        _footprint.Clear();
        _onConfirmed = null; // 취소로 끝났으면 확정 콜백은 실행하지 않는다(재료 보존).
    }
}
