using System.Collections.Generic;
using UnityEngine;
using CombatSpace;
using NorthLand.Combat;

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
    [SerializeField] private Color rangeColor = new Color(0.2f, 0.8f, 1f, 0.9f);      // 외곽선(굵게)
    [SerializeField] private Color rangeFillColor = new Color(0.2f, 0.8f, 1f, 0.15f); // 채움(반투명)

    [Header("셀 하이라이트")]
    [SerializeField] private Color validCellColor = new Color(0.2f, 1f, 0.3f, 0.35f);
    [SerializeField] private Color invalidCellColor = new Color(1f, 0.25f, 0.2f, 0.35f);

    private readonly List<GameObject> _cellHighlights = new List<GameObject>();
    private Material _cellMatValid;
    private Material _cellMatInvalid;
    private TowerPlacementData _activeData; // 현재 배치 중인 타워의 데이터(주입 또는 더미)
    // 현재 배치 중인 타워의 원본 SO. 확정 시 Tower.Build에 넘겨 인스턴스를 이 SO로 조립한다 —
    // "패널에서 산 것"과 "실제로 배치된 것"을 같게 만드는 유일한 연결선이다(WL-129).
    private TowerAsset _activeAsset;
    private IReadOnlyList<ResourceCost> _activeCost; // 현재 배치 중인 타워의 비용(확정 시 차감)
    private System.Action _onConfirmed; // 배치 확정 후 1회 콜백(합성 재료 소모 등). 확정 직후 소비하고 비운다.
    // 배치 세션 종료(확정 복귀·취소·다른 배치로 교체) 1회 콜백. 확정 여부와 무관하게 "이 배치는 끝났다"만 알린다.
    // 합성이 클릭 시점에 고정한 핑크 프리뷰(#213 §5.3)를 되돌리는 신호로 쓴다 — 확정/취소 어느 쪽으로 끝나든 필요.
    private System.Action _onEnded;
    private ManagementController _management; // 자원 차감 게이트웨이(WL-017). null이면 무료 배치(테스트 씬).

    // 프레임당 풋프린트를 1회만 계산해 캐시(스냅에서 채우고, 검증·하이라이트가 공유).
    // MouseManager는 매 프레임 Snap → CanPlaceAt 순으로 호출하므로 CanPlaceAt은 이 캐시를 신뢰한다.
    private readonly List<(Vector3 pos, BattleTile tile)> _footprint = new List<(Vector3, BattleTile)>();
    // OverlapSphere 재사용 버퍼(배치 중 매 프레임 힙 할당 방지). 셀 하나에 겹치는 콜라이더는 소수.
    private readonly Collider[] _overlap = new Collider[8];

    //Ksj
    //타워가 여러 버프 타일을 점유할 때 사용할 효과 중첩 규칙
    [Header("Tile Buff")]
    [SerializeField]
    private TileBuffRuleSettings tileBuffRules;

    /// 셀 간격(월드). Awake에서 신맵 설정을 단일 출처로 해석해 둔 값이라, 합성 연출(#265)처럼
    /// "타일 한 칸"을 기준 길이로 써야 하는 쪽이 같은 해석을 다시 하지 않도록 노출한다.
    public float TileSize => tileSize;

    private NorthLand.Combat.RangeCircle _rangeCircle;

    private BattleTile lastPreviewAnchor;

    private bool previewFootprintInitialized;

    private readonly TileBuffCalculator previewBuffCalculator = new TileBuffCalculator();

    private readonly List<BuffTileDefinition> previewDefinitions = new List<BuffTileDefinition>();

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
        if (_rangeCircle != null) Destroy(_rangeCircle.gameObject);
        ClearCellHighlights();
        if (_cellMatValid != null) Destroy(_cellMatValid);
        if (_cellMatInvalid != null) Destroy(_cellMatInvalid);
    }

    // ── 진입점 ─────────────────────────────────────────────────────────────────────
    /// 더미 데이터 + 인스펙터 프리팹으로 배치 시작. UI 버튼 OnClick에 연결(현재 테스트 경로).
    /// 비용은 so.Cost, 확정 콜백 없음(일반 타워 배치).
    public bool BeginTowerPlacement(TowerAsset so)
        => BeginTowerPlacement(so, so != null ? so.Cost : null, null);

    /// 비용·확정 콜백을 주입하는 오버로드. 합성(#195)이 결과 타워를 결과 코스트(ExtraCost)로 배치하고,
    /// 배치 확정 직후 onConfirmed(재료 소모)를 실행하는 데 쓴다. 배치 코어는 단일인자 경로와 동일.
    /// onEnded는 확정/취소 무관하게 배치 세션이 끝날 때 1회. **반환값 = 배치 세션이 실제로 시작됐는가** —
    /// false면 onEnded도 영영 오지 않으므로, 호출부가 배치 동안 유지하려던 상태(합성 핑크 고정 등)를
    /// 걸어두면 안 된다는 신호다.
    public bool BeginTowerPlacement(TowerAsset so, IReadOnlyList<ResourceCost> cost, System.Action onConfirmed, System.Action onEnded = null)
    {
        if (so == null)
        {
            Debug.LogError("[TowerPlacer] null TowerAsset은 배치할 수 없습니다.");
            return false;
        }

        // Data는 패널(TowerSelectPanelView)이 SO 주입 시 채운다 — 여기선 방어만.
        if (so.Data == null)
        {
            Debug.LogError($"[TowerPlacer] TowerData 미주입(TowerID={so.TowerID}) — 패널에서 SO 주입 시 Data를 채웠는지 확인하세요.");
            return false;
        }

        towerPrefab = so.TowerPrefab;
        ghostPrefab = so.GhostPrefab;
        _activeCost = cost;
        _activeAsset = so;
        // _onConfirmed은 StartPlacement가 BeginPlacement '이후'에 설정한다 — BeginPlacement 내부의
        // CancelPlacement가 이전 배치의 OnEnded=EndPlacement를 발화해 _onConfirmed을 null로 지우므로,
        // 여기서 미리 대입하면 합성 재료 소모 콜백이 유실된다(무료 합성 버그).

        // 프리뷰 반경. `TowerType→AttackFields` 해석은 여기서 다시 분기하지 않고
        // `TowerBehaviourFactory.ResolveAttackFields` 단일 출처를 쓴다(WL-079) — 예전에는 이 switch가
        // 같은 해석의 4번째 복제였고, 새 타워 타입을 추가할 때 빠뜨리기 쉬운 자리였다.
        TowerAsset.AttackFields attack = NorthLand.Combat.TowerBehaviourFactory.ResolveAttackFields(so);
        float previewRange;

        if (attack != null)
        {
            previewRange = attack.AttackRange;
        }
        else if (so.TowerType == TowerType.Magic)
        {
            // 마법 타워는 오라 반경을 사거리 미리보기로 사용(#111 완료기준 #4).
            // 반경 규칙은 TowerAsset.MagicRadius 단일 출처(WL-056) — 오라 행동의 실효과와 공유.
            previewRange = so.MagicRadius;
        }
        else
        {
            Debug.LogError($"[TowerPlacer] 공격 스탯도 오라 반경도 해석할 수 없는 TowerType={so.TowerType}입니다.");
            return false;
        }

        return StartPlacement(new(so.Data.GridWidth, so.Data.GridHeight, previewRange), onConfirmed, onEnded);
    }

    // 게이트웨이(예정): tower/ghost 프리팹 + footprint/range를 담은 SO가 생기면 아래 오버로드를 추가한다.
    //   public void BeginTowerPlacement(TowerXxxSO so) {
    //       towerPrefab = so.TowerPrefab; ghostPrefab = so.GhostPrefab;                 // SO가 프리팹까지 제공
    //       StartPlacement(new TowerPlacementData(so.GridWidth, so.GridHeight, so.Range));
    //   }
    // 이러면 위 더미 경로를 대체하며, 배치·검증·미리보기 코어(StartPlacement 이하)는 무수정이다.

    // 실제 배치 시작 코어(진입 방식과 무관). 게이트웨이/더미 어느 경로든 이 메서드를 호출한다.
    // onConfirmed(합성 재료 소모 등)는 BeginPlacement 이후에 설정한다(순서 주의 — 아래 참고).
    private bool StartPlacement(TowerPlacementData data, System.Action onConfirmed = null, System.Action onEnded = null)
    {
        if (MouseManager.Instance == null)
        {
            Debug.LogError("[TowerPlacer] MouseManager가 씬에 없습니다.");
            return false;
        }

        if (ghostPrefab == null || towerPrefab == null)
        {
            Debug.LogError("[TowerPlacer] ghostPrefab/towerPrefab을 인스펙터에서 지정하세요.");
            return false;
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

        // 확정/종료 콜백은 반드시 BeginPlacement '이후'에 설정한다 — 위 BeginPlacement 내부의
        // CancelPlacement→이전 배치 EndPlacement가 _onConfirmed/_onEnded를 소비·null 처리하기 때문
        // (프리뷰와 동일한 순서 이슈). 이 순서 덕분에 "이전 배치 종료 통지 → 새 배치 콜백 등록"이 보장된다.
        _onConfirmed = onConfirmed;
        _onEnded = onEnded;

        // 프리뷰는 BeginPlacement 이후에 만든다.
        // (BeginPlacement 내부의 CancelPlacement가 이전 배치의 OnEnded=EndPlacement를 먼저 발화해
        //  방금 만든 프리뷰를 지우는 순서 문제를 피하기 위함)
        lastPreviewAnchor = null;
        previewFootprintInitialized = false;
        CreateRangeIndicator(_activeData.AttackRange);
        CreateCellHighlights(_activeData.GridWidth * _activeData.GridHeight);
        return true;
    }

    // ── 스냅: 앵커(히트 타일) 기준 W×H 풋프린트의 중심 월드 좌표 ─────────────────────
    // 그리드가 월드 X/Z축에 정렬돼 있다고 가정한다(battlespace 회전 없음). 프리뷰도 여기서 갱신.
    // y는 hit.point.y(레이가 타일 옆면에 맞으면 벽면 높이) 대신 타일 앵커 y를 써서 타워가 항상 윗면에 앉는다.
    private Vector3 SnapToFootprintCenter(RaycastHit hit)
    {
        BattleTile anchor = hit.collider.GetComponentInParent<BattleTile>();

        bool footprintChanged = !previewFootprintInitialized || anchor != lastPreviewAnchor;

        if (footprintChanged)
        {
            RebuildFootprint(anchor);

            lastPreviewAnchor = anchor;
            previewFootprintInitialized = true;

            UpdateRangeIndicator(CalculatePreviewRange());
        }

        Vector3 result = anchor != null
            ? new Vector3(
                anchor.transform.position.x + (_activeData.GridWidth - 1) * 0.5f * tileSize,
                anchor.AnchorPosition.y,
                anchor.transform.position.z + (_activeData.GridHeight - 1) * 0.5f * tileSize)
            : hit.point;

        if (_rangeCircle != null)
        {
            _rangeCircle.transform.position = result;
            _rangeCircle.Show(); // 커서가 유효 타일 위에 온 첫 스냅부터 표시(원점 잔상 방지)
        }
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
        // 배치된 타워를 합성(#183) 그룹 선택 대상으로 표시(마커 런타임 부착).
        // 합성 결과 타워도 이 경로로 배치되므로 다단 합성의 재료가 될 수 있다.
        placed.AddComponent<TowerGroupSelectable>();
        foreach ((Vector3 _, BattleTile tile) in _footprint)
        {
            occupant.Occupy(tile);
        }

        //KSJ
        // 타워가 점유한 모든 타일의 버프를 중첩 규칙에 따라 계산하고,
        // 계산 결과를 배치된 타워의 TowerTileBuff 컴포넌트에 저장한다.
        TowerTileBuff towerTileBuff = placed.GetComponent<TowerTileBuff>();

        if (towerTileBuff == null)
        {
            towerTileBuff = placed.AddComponent<TowerTileBuff>();
        }

        // ⚠ 순서 주의: 타일 버프를 Tower.Build **앞에** 적용한다. 버프 오라는 조립 시점에 자기 반경으로
        // 대상을 한 번 훑는데, 그 반경이 타일 버프(사거리)에 의존하기 때문이다. 순서가 뒤바뀌면
        // 첫 적용이 버프 이전 반경으로 계산된다 — 구 AuraTower가 Start에서 반경을 재계산하던 우회로가
        // 정확히 이 순서 문제였다. 여기서 한 번 정해두면 그 우회로가 필요 없다.
        towerTileBuff.Initialize(CalculateTileBuff(occupant.Tiles));

        // 배치 확정된 SO로 조립한다. **패널에서 산 SO가 프리팹이 물고 있는 SO를 이긴다** —
        // 둘이 다르면 Tower.Build가 경고를 내고 산 쪽으로 재조립한다(WL-129의 무증상 불일치 해소).
        if (placed.TryGetComponent(out NorthLand.Combat.Tower placedTower))
        {
            placedTower.Build(_activeAsset);
        }
        else
        {
            Debug.LogError(
                $"[TowerPlacer] 배치한 프리팹({placed.name})에 Tower 컴포넌트가 없습니다 — " +
                "게임플레이가 동작하지 않습니다. 타워 프리팹 구성을 확인하세요.", placed);
        }

        // 확정 콜백(합성 재료 소모 등)은 배치 성공 후 1회만 실행한다.
        // 먼저 비우고 호출해 연속 배치(keepPlacing)에서도 재실행되지 않게 한다.
        var confirmed = _onConfirmed;
        _onConfirmed = null;
        confirmed?.Invoke();

        // 등장 연출(#264)은 **로직이 전부 끝난 뒤** 마지막에 얹는다. 시각 전용·논블로킹이라 여기서
        // 기다리지 않고, 연출 도중 밤 전환이나 새 배치가 들어와도 타워는 이미 완성 상태다.
        // 바닥 링 크기는 bounds가 아니라 풋프린트에서 준다 — 타워 에셋이 교체돼도 "몇 칸을 먹었는지"는
        // 안 바뀌는 값이라, 홀쭉한 에셋이 와도 링이 칸보다 작아지지 않는다.
        float footprintSize = Mathf.Max(_activeData.GridWidth, _activeData.GridHeight) * tileSize;
        NorthLand.Combat.TowerSpawnEffect.Play(placed.transform, footprintSize);
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

    // ── 사거리 미리보기(공용 RangeCircle: 채움+외곽선) ──────────────────────────────────
    private void CreateRangeIndicator(float range)
    {
        if (_rangeCircle != null) Destroy(_rangeCircle.gameObject);

        _rangeCircle = NorthLand.Combat.RangeCircle.Create(null, rangeFillColor, rangeColor, "TowerRangePreview");
        _rangeCircle.SetRadius(range);
        _rangeCircle.Hide(); // 첫 스냅 전엔 맵 원점(0,0)에 원이 노출되므로 숨긴다 — SnapToFootprintCenter에서 Show
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
        if (_rangeCircle != null) Destroy(_rangeCircle.gameObject);
        _rangeCircle = null;
        ClearCellHighlights();
        _footprint.Clear();
        _onConfirmed = null; // 취소로 끝났으면 확정 콜백은 실행하지 않는다(재료 보존).
        lastPreviewAnchor = null;
        previewFootprintInitialized = false;

        // 종료 통지는 정리 뒤 마지막에, 먼저 비우고 호출한다 — 구독자가 이 콜백 안에서 새 배치를 시작해도
        // (또는 EndPlacement가 재진입해도) 같은 콜백이 두 번 불리지 않게.
        var ended = _onEnded;
        _onEnded = null;
        ended?.Invoke();
    }

    private float CalculatePreviewRange()
    {
        previewDefinitions.Clear();

        foreach ((Vector3 _, BattleTile tile) in _footprint)
        {
            BuffTileDefinition definition = GetBuffDefinition(tile);

            if (definition != null)
            {
                previewDefinitions.Add(definition);
            }
        }

        TileBuffCalculationResult result = previewBuffCalculator.Calculate(previewDefinitions, tileBuffRules);

        float flat = result.GetValue(TileBuffStat.AttackRange, TileModifierMode.Flat);

        float percentage = result.GetValue(TileBuffStat.AttackRange, TileModifierMode.Percentage);

        return (_activeData.AttackRange + flat) * (1f + percentage / 100f);
    }

    // 반지름 갱신은 RangeCircle이 자체적으로 같은 값 재생성을 생략한다(중복 캐시 불필요).
    private void UpdateRangeIndicator(float range)
    {
        if (_rangeCircle != null) _rangeCircle.SetRadius(range);
    }

    private static BuffTileDefinition GetBuffDefinition(BattleTile tile)
    {
        if (tile == null)
        {
            return null;
        }

        CombatMapTileView tileView = tile.GetComponentInParent<CombatMapTileView>();

        return tileView != null ? tileView.BuffDefinition : null;
    }

    private TileBuffCalculationResult CalculateTileBuff(IReadOnlyList<BattleTile> tiles)
    {
        previewDefinitions.Clear();

        if (tiles != null)
        {
            foreach (BattleTile tile in tiles)
            {
                BuffTileDefinition definition = GetBuffDefinition(tile);

                if (definition != null)
                {
                    previewDefinitions.Add(definition);
                }
            }
        }

        return previewBuffCalculator.Calculate(previewDefinitions, tileBuffRules);
    }

}
