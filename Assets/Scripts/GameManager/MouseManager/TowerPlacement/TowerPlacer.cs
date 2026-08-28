using System.Collections.Generic;
using UnityEngine;
using CombatSpace;
using NorthLand.Combat;
using UnityEngine.SceneManagement;

/// 배치에 필요한 타워 데이터(풋프린트·사거리)의 최소 단위.
/// TowerPlacer는 이 구조체에만 의존하고 특정 SO(TowerAsset/Combat TowerData)에 묶이지 않는다.
/// → 데이터 출처를 자유롭게 교체 가능. 지금은 더미로 채운다.
/// (나중에 tower/ghost 프리팹 + footprint/range를 담은 SO를 게이트웨이로 주입받아 채운다 — TowerPlacer 참고.)
public readonly struct TowerPlacementData
{
    public readonly int GridWidth;    // 풋프린트 가로(셀 수)
    public readonly int GridHeight;   // 풋프린트 세로(셀 수)
    public readonly float AttackRange; // 공격·빔 사거리(월드 반경). 0 = 이 타워에 그 축이 없음
    // 오라(장판) 반경. 사거리와 **따로** 싣는 이유는 타일 버프 축이 갈려 있어서다(TowerStat.AuraRadius) —
    // 합친 값 하나만 들면 어느 축의 modifier를 얹어야 할지 알 수 없고, 프리뷰가 실제와 어긋난다.
    public readonly float AuraRadius;

    // 버프 없는 상태의 미리보기 원 반경 = 두 축 중 큰 쪽(TowerAsset.PreviewRadius와 같은 규칙).
    public float PreviewRadius => Mathf.Max(AttackRange, AuraRadius);
    // 모델을 그리드 축에서 Y축으로 돌릴 각도(도). 출처는 `TowerAsset.PlacementYaw` 하나고, 고스트와
    // 본체가 **같은 값**을 읽어야 미리보기와 실제 배치가 어긋나지 않아서 여기까지 실어 온다.
    public readonly float PlacementYaw;

    // ⚠ **기본값을 주지 않는다.** 예전에는 yaw에 기본값 0이 있었는데, 뒤에 `auraRadius`를 그 앞으로
    // 끼워 넣는 순간 `new(w, h, range, so.PlacementYaw)` 호출부가 **컴파일을 통과하면서** yaw를
    // 반경으로 넘겼다 — 실제로 아처·캐논(yaw 45)의 고스트 사거리 원이 45로 그려졌고, 축약 생성자
    // `new(...)`라 grep에도 걸리지 않았다. 전부 필수 인자로 두면 같은 실수가 컴파일 에러로 잡힌다.
    public TowerPlacementData(int gridWidth, int gridHeight, float attackRange, float auraRadius,
        float placementYaw)
    {
        GridWidth = Mathf.Max(1, gridWidth);
        GridHeight = Mathf.Max(1, gridHeight);
        AttackRange = attackRange;
        AuraRadius = auraRadius;
        PlacementYaw = placementYaw;
    }
}

/// 이 배치의 되돌리기 커맨드를 누가 소유하는가(#281).
///
/// **암묵적 판정(`onConfirmed != null`)을 쓰지 않는 이유**: 확정 콜백을 쓰는 세 번째 소비처가 생기는
/// 순간 그 배치가 히스토리에 오를지 말지가 **조용히** 갈린다. 이중 등록이면 한 조작이 두 번 되감기고,
/// 미등록이면 영영 되돌릴 수 없다 — 둘 다 증상이 원인에서 멀다. 그래서 명시적 신호로 받고,
/// `BeginTowerPlacement`에 **기본값을 주지 않는다**.
public enum PlacementOwner
{
    /// 배치 자신. 확정 즉시 커맨드를 히스토리에 올린다(패널에서 고른 일반 타워 배치).
    Placer,

    /// 호출부. 커맨드를 만들어 확정 콜백으로 넘기기만 하고 히스토리엔 올리지 않는다.
    /// 합성이 이것을 쓴다 — 결과 배치가 따로 올라가면 한 번의 합성이 두 번에 나눠 되감긴다.
    Caller,
}

/// 타워 배치 어댑터: 전투 공간 그리드의 허용 셀(건설 가능 타일)에 W×H 풋프린트로 타워를 배치한다.
/// MouseManager의 배치 흐름(PlacementRequest)을 재사용하며, 타일 종류·점유는 BattleTile로 판정한다.
/// 자원 차감·낮 전용 게이팅은 훅 지점만 표시(TODO) — Docs/Core/TowerPlacement.md §8.
public class TowerPlacer : MonoBehaviour
{

    [Header("세이브 복원")]
    [SerializeField]
    private CombatMapTileSpawner combatMapTileSpawner;

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
    // 배치 확정 후 1회 콜백(합성 결과 편입 등). 확정 직후 소비하고 비운다.
    // 인자는 **방금 배치된 타워의 되돌리기 커맨드**다(#281). 합성이 그것을 자기 커맨드에 편입해
    // 결과 타워까지 한 번에 되돌리는 데 쓴다. 합성 연출(#265)이 필요로 하는 배치된 Transform은
    // `TowerPlaceCommand.Placed`로 그대로 얻는다 — 등장 연출이 스케일을 0으로 만들기 **전에** 재야
    // 하므로 호출 순서가 등장 연출보다 앞이라는 계약도 그대로다(PlaceTower 참고).
    private System.Action<TowerPlaceCommand> _onConfirmed;
    // 배치 세션 종료(확정 복귀·취소·다른 배치로 교체) 1회 콜백. 확정 여부와 무관하게 "이 배치는 끝났다"만 알린다.
    // 합성이 클릭 시점에 고정한 핑크 프리뷰(#213 §5.3)를 되돌리는 신호로 쓴다 — 확정/취소 어느 쪽으로 끝나든 필요.
    private System.Action _onEnded;
    // 이번 배치의 되돌리기 커맨드를 누가 소유하는가(#281). _onConfirmed/_onEnded와 같은 순서 계약을 탄다.
    private PlacementOwner _historyOwner = PlacementOwner.Placer;
    private ManagementController _management; // 자원 차감 게이트웨이(WL-017). null이면 무료 배치(테스트 씬).

    // 그리드 기준축의 출처. `CombatMapTileSpawner.CoordinateRoot`(= 타일들의 부모)의 회전이다 —
    // 타일은 그 아래에 `localRotation = identity`로 생성되므로 루트 회전이 곧 셀 축이다.
    // 월드 X/Z를 그대로 쓰면 맵을 조금만 돌려도 셀 뷰가 타일과 어긋난다(실제 발생: 맵 Y 59.45°).
    //
    // 왜 앵커 타일이 아니라 루트인가: 앵커 타일의 회전을 읽으면 "타일 로컬 회전이 항상 0"이라는
    // 전제에 기대게 된다. 타일 프리팹에 랜덤 yaw(반복감 제거)가 들어오면 셀마다 축이 튀어
    // 이웃 셀을 엉뚱한 방향으로 짚는다 — `tileSize`와 같은 출처(스포너)에서 받아 그 전제를 없앤다.
    // 스포너가 없는 씬(구맵·테스트)에서는 identity, 즉 기존 월드 축 동작으로 떨어진다.
    private Transform _gridRoot;

    private Quaternion GridBasis => _gridRoot != null ? _gridRoot.rotation : Quaternion.identity;

    // 프레임당 풋프린트를 1회만 계산해 캐시(스냅에서 채우고, 검증·하이라이트가 공유).
    // MouseManager는 매 프레임 Snap → CanPlaceAt 순으로 호출하므로 CanPlaceAt은 이 캐시를 신뢰한다.
    private readonly List<(Vector3 pos, BattleTile tile)> _footprint = new List<(Vector3, BattleTile)>();
    // OverlapSphere 재사용 버퍼(배치 중 매 프레임 힙 할당 방지). 셀 하나에 겹치는 콜라이더는 소수.
    private readonly Collider[] _overlap = new Collider[8];

    [Header("버프 타일 아이콘")]
    [SerializeField]
    private BuffTileIconPreview buffTileIconPreview;

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

        // 그리드 축도 간격과 같은 출처(스포너)에서 받는다 — 근거는 _gridRoot 주석.
        var tileSpawner = FindFirstObjectByType<CombatSpace.CombatMapTileSpawner>();
        if (tileSpawner != null)
        {
            _gridRoot = tileSpawner.CoordinateRoot;
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
    /// [튜토리얼·디버그용] 켜면 이후 시작하는 배치가 비용을 지불하지 않는다.
    /// 무료화 메커니즘은 아래 5인자 오버로드에 cost=null을 넘기는 것 하나뿐이고(F4 디버그 패널이
    /// DebugTowerSection.Place에서 쓰는 그 경로다), 이 스위치는 같은 경로를 정식 타워 패널에도 열어 줄 뿐이다.
    /// 되돌리기도 정합하다 — TowerPlaceCommand의 지불 기록이 비어 있으므로 Undo 환원도 0이 된다.
    ///
    /// ⚠ 타워 패널(TowerSelectPanelView)의 자원 게이트는 **별개 축**이다. 이것만 켜고 패널이 모르면
    ///   자원이 모자랄 때 버튼이 회색이라 배치를 시작조차 못 한다 — 패널이 이 값을 함께 본다.
    public bool FreePlacement { get; set; }

    /// 더미 데이터 + 인스펙터 프리팹으로 배치 시작. UI 버튼 OnClick에 연결(현재 테스트 경로).
    /// 비용은 so.Cost(FreePlacement면 무료), 확정 콜백 없음(일반 타워 배치).
    public bool BeginTowerPlacement(TowerAsset so)
        => BeginTowerPlacement(so, so != null && !FreePlacement ? so.Cost : null, null, null, PlacementOwner.Placer);

    /// 비용·확정 콜백을 주입하는 오버로드. 합성(#195)이 결과 타워를 결과 코스트(ExtraCost)로 배치하고,
    /// 배치 확정 직후 onConfirmed(결과 커맨드 편입)를 실행하는 데 쓴다. 배치 코어는 단일인자 경로와 동일.
    /// onEnded는 확정/취소 무관하게 배치 세션이 끝날 때 1회. **반환값 = 배치 세션이 실제로 시작됐는가** —
    /// false면 onEnded도 영영 오지 않으므로, 호출부가 배치 동안 유지하려던 상태(합성 핑크 고정 등)를
    /// 걸어두면 안 된다는 신호다.
    /// historyOwner에 **기본값이 없는 것은 의도적이다** — PlacementOwner 주석 참고.
    public bool BeginTowerPlacement(TowerAsset so, IReadOnlyList<ResourceCost> cost,
        System.Action<TowerPlaceCommand> onConfirmed, System.Action onEnded, PlacementOwner historyOwner)
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

        // 프리뷰 반경은 **두 축을 따로** 넘긴다. 합친 값 하나(`so.PreviewRadius`)만 주면 타일 버프를 얹을 때
        // 어느 축의 modifier를 쓸지 알 수 없어 프리뷰가 배치 후와 어긋난다(TowerPlacementData.AuraRadius 참조).
        // 호출부가 여전히 타워 종류를 모르는 것은 그대로다 — SO가 축별 기본값으로 답한다(#274, WL-056).
        return StartPlacement(
            new(so.Data.GridWidth, so.Data.GridHeight, so.AttackSideRadius, so.AuraSideRadius, so.PlacementYaw),
            onConfirmed, onEnded, historyOwner);
    }

    // 게이트웨이(예정): tower/ghost 프리팹 + footprint/range를 담은 SO가 생기면 아래 오버로드를 추가한다.
    //   public void BeginTowerPlacement(TowerXxxSO so) {
    //       towerPrefab = so.TowerPrefab; ghostPrefab = so.GhostPrefab;                 // SO가 프리팹까지 제공
    //       StartPlacement(new TowerPlacementData(so.GridWidth, so.GridHeight, so.Range));
    //   }
    // 이러면 위 더미 경로를 대체하며, 배치·검증·미리보기 코어(StartPlacement 이하)는 무수정이다.

    // 실제 배치 시작 코어(진입 방식과 무관). 게이트웨이/더미 어느 경로든 이 메서드를 호출한다.
    // onConfirmed(합성 재료 소모 등)는 BeginPlacement 이후에 설정한다(순서 주의 — 아래 참고).
    private bool StartPlacement(TowerPlacementData data, System.Action<TowerPlaceCommand> onConfirmed,
        System.Action onEnded, PlacementOwner historyOwner)
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
            // 회전된 맵에서 고스트가 타일과 각이 맞게(배치 세션 동안 상수) + SO가 지정한 모델 yaw.
            // 아래 PlaceTower의 본체 회전과 **같은 출처**를 써야 미리보기와 실제 배치가 어긋나지 않는다.
            GhostRotation = GridBasis * Quaternion.Euler(0f, data.PlacementYaw, 0f),
            Snap = SnapToFootprintCenter,
            CanPlaceAt = CanPlaceFootprint,
            OnConfirmed = PlaceTower,
            OnRejected = Sfx.Rejected,
            OnSurfaceHoverChanged = SetPreviewVisible,
            OnEnded = EndPlacement,
            KeepPlacingAfterConfirm = keepPlacing,
        });

        // 확정/종료 콜백은 반드시 BeginPlacement '이후'에 설정한다 — 위 BeginPlacement 내부의
        // CancelPlacement→이전 배치 EndPlacement가 _onConfirmed/_onEnded를 소비·null 처리하기 때문
        // (프리뷰와 동일한 순서 이슈). 이 순서 덕분에 "이전 배치 종료 통지 → 새 배치 콜백 등록"이 보장된다.
        // 소유권도 같은 자리에서 세운다 — 콜백과 한 세트로 이번 세션을 규정하는 값이다(#281).
        _onConfirmed = onConfirmed;
        _onEnded = onEnded;
        _historyOwner = historyOwner;

        // 프리뷰는 BeginPlacement 이후에 만든다.
        // (BeginPlacement 내부의 CancelPlacement가 이전 배치의 OnEnded=EndPlacement를 먼저 발화해
        //  방금 만든 프리뷰를 지우는 순서 문제를 피하기 위함)
        lastPreviewAnchor = null;
        previewFootprintInitialized = false;
        if (buffTileIconPreview != null)
        {
            buffTileIconPreview.ShowAll();
        }
        CreateRangeIndicator(_activeData.PreviewRadius);
        CreateCellHighlights(_activeData.GridWidth * _activeData.GridHeight);
        return true;
    }

    /// <summary>
    /// 기준 타일과 타워 풋프린트 크기로 실제 타워 중심 위치를 계산한다.
    /// 일반 배치와 세이브 복원이 동일한 좌표 계산식을 사용한다.
    /// </summary>
    private Vector3 CalculateFootprintCenter(BattleTile anchor,TowerPlacementData data)
    {
        if (anchor == null)
            return Vector3.zero;

        Vector3 center = GridStep(
            anchor.transform.position,
            (data.GridWidth - 1) * 0.5f,
            (data.GridHeight - 1) * 0.5f);

        return new Vector3(center.x,anchor.AnchorPosition.y,center.z);
    }

    // ── 스냅: 앵커(히트 타일) 기준 W×H 풋프린트의 중심 월드 좌표 ─────────────────────
    // 그리드 축은 타일 루트에서 가져온다(`GridBasis`) — 맵 루트가 회전돼 있어도 맞는다. 프리뷰도 여기서 갱신.
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

        Vector3 result = anchor != null? CalculateFootprintCenter(anchor, _activeData) : hit.point;

        if (_rangeCircle != null)
        {
            _rangeCircle.transform.position = result;

            // 커서가 유효 타일 위에 온 첫 스냅부터 표시(원점 잔상 방지).
            // ⚠ 앵커가 없으면(배치 마스크는 맞혔지만 BattleTile이 아닌 표면) 놓을 자리가 없으므로 숨긴다 —
            //   무조건 Show하면 타일 아닌 곳에서 원이 커서를 따라다닌다.
            if (anchor != null) _rangeCircle.Show();
            else _rangeCircle.Hide();
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
        if (anchor == null)
            return;

        CombatMapTileView anchorView = anchor.GetComponentInParent<CombatMapTileView>();

        if (anchorView == null)
        {
            Debug.LogError("[TowerPlacer] 기준 타일의 CombatMapTileView를 찾을 수 없습니다.",anchor);

            return;
        }

        RebuildFootprint(anchor);

        foreach ((Vector3 _, BattleTile tile) in _footprint)
        {
            // 여기 도달하면 CanPlaceFootprint를 통과한 뒤 상태가 바뀐 것이다(방어). 플레이어에게는
            // 유효해 보이는 자리를 클릭했는데 아무 일도 안 난 상황이므로 거절음까지 내 준다.
            if (!IsBuildable(tile)) { Sfx.Rejected(); return; }
        }

        // 자원 차감(경영 게이트웨이 경유 — TowerPlacement.md §8). 성공 시에만 생성·점유한다.
        // 부족하면 배치 취소. 경영이 씬에 없으면(null) 무료 배치(테스트 씬).
        if (_management != null && !_management.TrySpend(_activeCost))
        {
            // CanPlaceFootprint가 이미 자원 부족을 걸러 여기 도달은 드묾(방어) — 조용한 실패 방지.
            Debug.Log("[TowerPlacer] 자원이 부족해 배치를 취소합니다.");
            Sfx.Rejected();
            return;
        }

        // 일반 배치와 세이브 복원이 동일한 생성 경로를 사용한다.
        if (!TryCreateTower(_activeAsset,anchor,anchorView,out GameObject placed))
        {
            Debug.LogError($"[TowerPlacer] 타워 생성에 실패했습니다: {_activeAsset?.TowerID}",this);

            return;
        }

        // 설치 성공음. **합성 결과 배치도 이 경로를 지나므로**(TowerFusionController가 결과 타워를
        // BeginTowerPlacement로 놓는다) 합성 완료음을 따로 낼 필요가 없다 — 여기 한 곳이 둘을 덮는다.
        Sfx.TowerInstalled();

        // 되돌리기 커맨드(#281). 배치는 이미 끝났고 이 커맨드는 그 결과를 **인수**한다 —
        // 실패해도 배치 자체는 정상이고 "되돌릴 수 없다"만 잃으므로 경고로 드러내고 진행한다.
        var command = new TowerPlaceCommand(placed, _activeCost, _management, tileSize);
        bool reversible = command.Execute();
        if (!reversible)
        {
            Debug.LogWarning("[TowerPlacer] 배치 커맨드 인수에 실패했습니다 — 이 배치는 되돌릴 수 없습니다(배치 자체는 정상).", placed);
        }

        // 확정 콜백(합성의 결과 편입 등)은 배치 성공 후 1회만 실행한다.
        // 먼저 비우고 호출해 연속 배치(keepPlacing)에서도 재실행되지 않게 한다.
        //
        // ⚠ 이 호출은 반드시 아래 등장 연출보다 **앞**이어야 한다. 합성 연출이 여기서 결과 타워의
        //   bounds를 재 수렴 목적지로 삼는데, 등장 연출은 시작하자마자 그 타워의 스케일을 0으로
        //   만들기 때문이다 — 순서가 뒤바뀌면 입자가 쪼그라든 상자의 중심으로 모인다.
        var confirmed = _onConfirmed;
        _onConfirmed = null;
        confirmed?.Invoke(command);

        // 히스토리 등록은 **명시적 소유권 신호**로 정한다(PlacementOwner 주석 참고).
        // 일반 배치에는 "확정을 기다리는 창"이 없다 — 배치가 선 그 순간이 곧 성공 종료다.
        // 인수에 실패한 커맨드는 올리지 않는다 — Undo가 아무 일도 안 하므로 눌러도 반응 없는
        // 슬롯 하나가 스택을 차지하고, 그 위의 진짜 조작이 한 칸 밀려 되돌려진다.
        if (reversible && _historyOwner == PlacementOwner.Placer) CommandHistory.Push(command);

        // 소유권은 확정 콜백과 마찬가지로 **1회성**이다 — 여기서 Placer로 내린다.
        // 연속 배치(keepPlacing, WL-105)에서 Caller가 남으면 2번째 이후 클릭이 만드는 결과 타워
        // 복제분이 AdoptResult도 Push도 받지 못해 **영구히 되돌릴 수 없다.** 복제분은 재료를 쓰지 않고
        // ExtraCost만 지불한 별개 배치이므로, 각자 독립 커맨드로 히스토리에 오르는 것이 옳다.
        // (EndPlacement의 리셋만으로는 못 막는다 — 그건 세션이 끝날 때만 돌고, 여기는 세션 안이다.)
        _historyOwner = PlacementOwner.Placer;

        // 등장 연출(#264)은 **로직이 전부 끝난 뒤** 마지막에 얹는다. 시각 전용·논블로킹이라 여기서
        // 기다리지 않고, 연출 도중 밤 전환이나 새 배치가 들어와도 타워는 이미 완성 상태다.
        // 바닥 링 크기는 bounds가 아니라 풋프린트에서 준다 — 타워 에셋이 교체돼도 "몇 칸을 먹었는지"는
        // 안 바뀌는 값이라, 홀쭉한 에셋이 와도 링이 칸보다 작아지지 않는다.
        // 알갱이 크기만 tileSize(한 칸) 기준으로 따로 넘긴다 — 풋프린트를 쓰면 다중 셀 타워에서만
        // 알갱이가 커져 합성 유입 입자와 크기가 어긋난다(#265, TowerSpawnEffect.Play 주석).
        float footprintSize = Mathf.Max(_activeData.GridWidth, _activeData.GridHeight) * tileSize;
        NorthLand.Combat.TowerSpawnEffect.Play(placed.transform, footprintSize, tileSize);
    }


    // StartMapTileRegistry의 등록 가능한 지형 조건과 동기화되어야 한다.
    // 추후 BattleTile의 공통 건설 가능 지형 프로퍼티로 통합한다.
    private static bool IsBuildable(BattleTile tile)
        => tile != null && tile.Kind == TileKind.Grass && !tile.Occupied;

    // 앵커 기준 (i, j)칸만큼 **그리드 축을 따라** 이동한 지점. y는 원점 값을 그대로 유지한다
    // (순수 yaw면 오프셋이 수평이라 어차피 유지되지만, 축이 기울어도 호출부의 y 규약을 깨지 않게 못박는다).
    private Vector3 GridStep(Vector3 origin, float i, float j)
    {
        Vector3 offset = GridBasis * new Vector3(i * tileSize, 0f, j * tileSize);
        return new Vector3(origin.x + offset.x, origin.y, origin.z + offset.z);
    }

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
                Vector3 cell = GridStep(a, i, j);
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
            // 회전은 UpdateCellHighlights가 매 프레임 그리드 축에 맞춘다(여기선 앵커를 아직 모른다).
            q.transform.localScale = Vector3.one * (tileSize * 0.9f);

            // 꺼진 채로 만든다 — 사거리 원을 Hide로 시작하는 것과 같은 이유다. 배치 버튼은 UI 위에 있어
            // 클릭한 프레임의 커서는 타일 위가 아니고, 첫 스냅 전까지 위치가 갱신되지 않아 **월드 원점에
            // 쿼드가 그대로 보인다.** 첫 스냅의 UpdateCellHighlights가 위치를 잡으면서 켠다.
            q.SetActive(false);

            _cellHighlights.Add(q);
        }
    }

    /// 배치 미리보기(풋프린트 하이라이트 + 사거리 원)를 한꺼번에 켜고 끈다.
    /// <see cref="PlacementRequest.OnSurfaceHoverChanged"/>가 부른다 — 커서가 타일 밖으로 나가면
    /// <c>Snap</c>이 아예 호출되지 않아서, 이 통지가 없으면 **마지막 타일 자리에 그대로 잔류한다.**
    private void SetPreviewVisible(bool visible)
    {
        // **켜는 일은 여기서 하지 않는다.** 표면 위에서는 Snap이 매 프레임 돌면서
        // UpdateCellHighlights로 필요한 쿼드만 켜고 사거리 원도 앵커 유무에 따라 켜고 끈다 —
        // 그쪽이 풋프린트 크기와 앵커를 아는 유일한 자리다. 여기서 한 번 더 켜면 Snap 뒤에 실행되면서
        // 여분 쿼드까지 되살려 옛 자리에 남긴다(호출 순서: Snap → 이 콜백).
        if (visible) return;

        if (_rangeCircle != null) _rangeCircle.Hide();

        foreach (GameObject q in _cellHighlights)
        {
            if (q != null) q.SetActive(false);
        }
    }

    // _footprint(스냅에서 계산됨)를 그대로 사용해 각 셀 하이라이트를 배치·색칠한다.
    // 풋프린트보다 많이 만들어 둔 여분(앵커가 없어 _footprint가 비는 경우 포함)은 꺼 둔다 —
    // 켠 채로 두면 위치를 갱신받지 못한 쿼드가 옛 자리에 남는다.
    private void UpdateCellHighlights()
    {
        for (int i = _footprint.Count; i < _cellHighlights.Count; i++)
        {
            GameObject spare = _cellHighlights[i];
            if (spare != null && spare.activeSelf) spare.SetActive(false);
        }

        for (int i = 0; i < _footprint.Count && i < _cellHighlights.Count; i++)
        {
            (Vector3 pos, BattleTile tile) = _footprint[i];
            GameObject q = _cellHighlights[i];
            // 표면을 벗어났다 돌아오면 SetPreviewVisible(false)가 꺼 둔 상태다 — 위치를 잡는 이 자리에서
            // 되살린다(켜는 주체를 한 곳에 모아 두면 "켜졌는데 위치가 옛날"이 생길 수 없다).
            if (!q.activeSelf) q.SetActive(true);
            // 하이라이트 표시 y를 타일 윗면(앵커)에 맞춘다 — 타워 배치 y와 일치. 타일 없으면 풋프린트 y 폴백.
            // (탐지용 RebuildFootprint/TileAt은 루트 y 유지 — 앵커가 콜라이더 위쪽일 때 OverlapSphere 놓침 방지)
            float topY = tile != null ? tile.AnchorPosition.y : pos.y;
            // 쿼드를 XZ 바닥에 눕히고(Euler 90) 그 위에 그리드 yaw를 얹는다 — 순서가 뒤바뀌면 축이 틀어진다.
            q.transform.SetPositionAndRotation(
                new Vector3(pos.x, topY + 0.03f, pos.z), // z-파이팅 방지 살짝 위로
                GridBasis * Quaternion.Euler(90f, 0f, 0f));
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
        if (buffTileIconPreview != null)
        {
            buffTileIconPreview.HideAll();
        }
        _footprint.Clear();
        _onConfirmed = null; // 취소로 끝났으면 확정 콜백은 실행하지 않는다(재료 보존).
        _historyOwner = PlacementOwner.Placer; // 콜백과 대칭으로 비운다(#281) — 세션 밖으로 새지 않게.
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

        // 두 축을 각자 합성해 큰 쪽을 쓴다 — 배치 후 `Tower.DisplayRange`가 액션별 값의 최댓값을
        // 고르는 것과 같은 규칙이라, 프리뷰 원과 배치 후 원이 같은 값을 가리킨다.
        return Mathf.Max(
            Buffed(_activeData.AttackRange, TileBuffStat.AttackRange),
            Buffed(_activeData.AuraRadius, TileBuffStat.AuraRadius));

        float Buffed(float baseValue, TileBuffStat stat)
        {
            // 없는 축은 0으로 남긴다. 이 가드가 없으면 오라 전용 타워(사거리 0)가 사거리 타일 위에서
            // flat 증가분만큼의 유령 원을 그려, 실제 오라 반경보다 큰 값이 Max를 이긴다.
            if (baseValue <= 0f) return 0f;

            float flat = result.GetValue(stat, TileModifierMode.Flat);

            float percentage = result.GetValue(stat, TileModifierMode.Percentage);

            return (baseValue + flat) * (1f + percentage / 100f);
        }
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

    /// <summary>
    /// 저장된 TowerAsset과 기준 셀 좌표를 이용해 타워를 복원한다.
    /// 비용 차감, Undo 등록, 입력 처리, 등장 연출은 수행하지 않는다.
    /// </summary>
    /// <param name="asset">TowerID로 조회한 타워 에셋.</param>
    /// <param name="anchorCell">저장된 풋프린트 기준 셀 좌표.</param>
    /// <param name="restoredTower">
    /// 복원된 타워. 실패하면 null.
    /// </param>
    /// <returns>타워 복원에 성공하면 true.</returns>
    public bool TryRestoreTower(TowerAsset asset,Vector2Int anchorCell,out NorthLand.Combat.Tower restoredTower)
    {
        restoredTower = null;

        if (combatMapTileSpawner == null)
        {
            Debug.LogError("[TowerPlacer] CombatMapTileSpawner가 연결되지 않았습니다.",this);

            return false;
        }

        if (asset == null)
        {
            Debug.LogError("[TowerPlacer] 복원할 TowerAsset이 없습니다.",this);

            return false;
        }

        if (!combatMapTileSpawner.TryGetTileView(anchorCell,out CombatMapTileView tileView))
        {
            Debug.LogError($"[TowerPlacer] 저장된 셀의 타일을 찾을 수 없습니다: {anchorCell}",this);

            return false;
        }

        if (tileView == null)
        {
            Debug.LogError($"[TowerPlacer] 셀의 타일 뷰가 비어 있습니다: {anchorCell}",this);

            return false;
        }

        BattleTile anchor = tileView.GetComponentInParent<BattleTile>();

        if (anchor == null)
        {
            Debug.LogError($"[TowerPlacer] 셀에 BattleTile이 없습니다: {anchorCell}",tileView);

            return false;
        }

        return TryRestoreTower(asset, anchor, out restoredTower);
    }

    /// <summary>
    /// 저장된 TowerAsset과 실제 기준 타일을 이용해 타워를 복원한다.
    /// 스타트맵처럼 자동 생성 그리드 좌표로 조회할 수 없는 타일에서 사용한다.
    /// </summary>
    public bool TryRestoreTower(
        TowerAsset asset,
        BattleTile anchor,
        out NorthLand.Combat.Tower restoredTower)
    {
        restoredTower = null;

        if (asset == null)
        {
            Debug.LogError("[TowerPlacer] 복원할 TowerAsset이 없습니다.", this);
            return false;
        }

        if (anchor == null)
        {
            Debug.LogError("[TowerPlacer] 복원할 기준 타일이 없습니다.", this);
            return false;
        }

        CombatMapTileView tileView =
            anchor.GetComponentInParent<CombatMapTileView>();

        if (tileView == null)
        {
            Debug.LogError(
                "[TowerPlacer] 기준 타일의 CombatMapTileView를 찾을 수 없습니다.",
                anchor);

            return false;
        }

        if (!TryCreateTower(asset,anchor,tileView,out GameObject placed))
        {
            Debug.LogError(
                $"[TowerPlacer] 저장된 타워 생성에 실패했습니다: {asset.TowerID}",
                this);

            return false;
        }

        if (!placed.TryGetComponent(out restoredTower))
        {
            Debug.LogError($"[TowerPlacer] 복원된 오브젝트에 Tower 컴포넌트가 없습니다: {asset.TowerID}",placed);

            Destroy(placed);
            restoredTower = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 검증된 기준 타일에 타워를 생성하고,
    /// 풋프린트 점유·타일 버프·Tower 조립을 수행한다.
    /// 자원 차감, Undo 등록, 등장 연출은 처리하지 않는다.
    /// </summary>
    private bool TryCreateTower(TowerAsset asset,BattleTile anchor,CombatMapTileView anchorView,out GameObject placed)
    {
        placed = null;


        if (asset == null ||anchor == null ||anchorView == null)
        {
            Debug.LogError("[TowerPlacer] 타워 생성에 필요한 데이터가 없습니다.",this);

            return false;
        }

        if (string.IsNullOrEmpty(asset.TowerID))
        {
            Debug.LogError("[TowerPlacer] TowerID가 비어 있어 타워를 생성할 수 없습니다.", asset);

            return false;
        }

        if (asset.Data == null)
        {
            asset.Data =DataTableManager.Get<TowerTable>("TowerTable")?.Get(asset.TowerID);
        }

        if (asset.Data == null)
        {
            Debug.LogError($"[TowerPlacer] TowerData를 찾을 수 없습니다: {asset.TowerID}",asset);

            return false;
        }

        _activeData = new TowerPlacementData(asset.Data.GridWidth,asset.Data.GridHeight,asset.AttackSideRadius,
            asset.AuraSideRadius,asset.PlacementYaw);

        Vector3 position =CalculateFootprintCenter(anchor,_activeData);

        GameObject prefab = asset.TowerPrefab;

        if (prefab == null)
        {
            Debug.LogError($"[TowerPlacer] TowerPrefab이 없습니다: {asset.TowerID}",asset);

            return false;
        }

        RebuildFootprint(anchor);

        foreach ((Vector3 _, BattleTile tile) in _footprint)
        {
            if (!IsBuildable(tile))
            {
                Debug.LogError($"[TowerPlacer] 배치할 수 없는 셀이 포함되어 있습니다: {anchorView.GridPosition}",anchor);

                return false;
            }
        }

        // 회전된 맵에서도 타워가 그리드 축과 동일한 방향을 바라보게 한다. 여기에 SO가 지정한 모델 yaw를
        // 얹는다 — 각도의 사유는 특정 에셋의 실루엣이므로 배치기가 상수로 들지 않는다(WL-180).
        // 세이브 복원·합성 결과 배치도 이 한 줄을 지나므로 경로마다 각도가 갈릴 수 없다.
        placed = Instantiate(prefab,position, GridBasis * Quaternion.Euler(0f, asset.PlacementYaw, 0f));

        // Additive 로딩 중에는 활성 씬이 LoadingScene이므로, 배치 대상 타일이 속한 씬으로
        // 명시적으로 이동한다. TowerPlacer가 DDOL이나 별도 UI 씬으로 옮겨져도 타워의 수명은
        // 자신이 점유한 타일과 함께해야 한다.
        SceneManager.MoveGameObjectToScene(placed, anchorView.gameObject.scene);

        var footprint = placed.AddComponent<TowerFootprint>();

        footprint.SetAnchorTile(anchor);
        footprint.SetAnchorCell(anchorView.GridPosition);

        placed.AddComponent<TowerGroupSelectable>();

        foreach ((Vector3 _, BattleTile tile) in _footprint)
        {
            footprint.Occupy(tile);
        }

        TowerTileBuff towerTileBuff = placed.GetComponent<TowerTileBuff>();

        if (towerTileBuff == null)
        {
            towerTileBuff = placed.AddComponent<TowerTileBuff>();
        }

        towerTileBuff.Initialize(CalculateTileBuff(footprint.Tiles));

        if (!placed.TryGetComponent(out NorthLand.Combat.Tower placedTower))
        {
            Debug.LogError($"[TowerPlacer] 배치한 프리팹에 Tower 컴포넌트가 없습니다: {asset.TowerID}",placed);

            Destroy(placed);
            placed = null;
            return false;
        }

        placedTower.Build(asset);
        return true;
    }

}
