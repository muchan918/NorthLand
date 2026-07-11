# SystemMap — 시스템 지도와 통합 계약 (PR 리뷰 필수 입력)

> **목적**: PR 리뷰 시 "이 변경이 누구의 어떤 시스템과 만나는가"를 판단하는 기준 문서.
> **갱신 규칙**: 시스템의 공개 API·계약이 바뀌는 PR은 이 문서를 **같은 PR에서** 갱신한다.
> 자동 리뷰 워크플로우(`.github/workflows/pr-review.yml`)가 매 리뷰마다 이 문서를 읽는다 —
> 낡은 지도는 리뷰 품질을 직접 해친다.

## 1. 시스템 및 소유자

| 시스템 | 소유자 | 경로 | 상태 |
|---|---|---|---|
| DataTable (CSV→static 레지스트리→SO) | muchan | `Assets/Personal/muchan` | Resource 1종 구현. Building/Tower/Skill/Reward 확장 예정 |
| Combat (타워/몬스터 공격·데미지) | SUNGSOO | `Assets/Personal/SUNGSOO/Scirpts/Combat` (폴더명 오타 주의 — WL-010) | 공격/데미지 코어만. 이동·사망처리·투사체 없음 |
| BattleMapBuilder (절차적 전투 맵) | SUNJIN | `Assets/Personal/SUNJIN/Scripts/MapBuilder` | 7×7 블록 경로 생성 구현. 싸이클 버그 해결이 다음 빌드 목표 |
| MouseManager (입력/선택/배치) | n0wst4ndup | `Assets/Personal/n0wst4ndup/MouseManager` | 2상태 머신 구현. Snap 항등·CanPlaceAt 항상 true (TODO) |
| Localization | n0wst4ndup | `Assets/Personal/n0wst4ndup/Localization` | 로케일 전환 테스트만 (ko-KR/en-US/ja-JP) |

## 2. 공개 API (다른 시스템이 소비해도 되는 것)

- `DataTableManager.Get<T>(string id)` — static. **null 반환 가능 → 호출부 null 체크 필수**
- `ResourceTable.Get(string id)` — null 반환 가능
- `ResourceAsset.Data` — **호출부가 Start()에서 직접 채우는 규약** (저장 안 됨)
- `IDamageable { Faction, IsDead, TakeDamage(DamageInfo) }`, `IAttacker`, `DamageInfo`,
  `Faction { Player, Enemy }` — namespace `NorthLand.Combat`
- `MouseManager.Instance.BeginPlacement(PlacementRequest)` / `CancelPlacement()` / `event OnSelectionChanged`
- `ISelectable { OnSelected(), OnDeselected() }`,
  `PlacementRequest { GhostPrefab, CanPlaceAt, OnConfirmed, KeepPlacingAfterConfirm }`
- `StageRoadTracker.RoadWorldPoints` — ⚠️ HashSet(순서 없음). **이동 경로로 사용 불가**
- MapBuilder의 **순서 있는 경로·스폰 지점·최종 목표 좌표는 아직 공개 API가 없음** (WL-003)

## 3. 접점 매트릭스 (왼쪽 시스템을 건드리는 PR은 오른쪽 항목을 실제 코드로 대조)

| 접점 | 확인할 것 |
|---|---|
| Combat ↔ BattleMapBuilder | 몬스터 이동 경로(순서 있는 경로 API 부재 — WL-003), 레이어(WL-005), 좌표계(battlespace 로컬 vs 월드 — WL-007) |
| Combat ↔ DataTable | 스탯 데이터 원본(CSV 파이프라인 vs 손입력 SO — WL-001) |
| MouseManager ↔ BattleMapBuilder | 그리드 스냅, CanPlaceAt·타일 종류 질의 API(WL-004), 좌표계(WL-007) |
| MouseManager ↔ Combat | 타워 배치(PlacementRequest→Tower 프리팹), 선택(ISelectable), TowerInfoUI 데이터 연동(WL-011) |
| DataTable ↔ Localization | 표시 문자열 소유권(CSV 한글 하드코딩 vs String Table 키 — WL-013) |
| 모든 시스템 ↔ 전역 설정 | 레이어/태그(`ProjectSettings/TagManager.asset` — WL-005), URP 설정(`Assets/Settings`), 패키지(`Packages/manifest.json`) |

## 4. 팀 계약 (위반 = 🔴 후보)

1. **입력 단일 창구**: 포인터/키보드 입력은 MouseManager만 읽는다. 게임플레이 코드의
   `Mouse.current`/`Keyboard.current` 직접 폴링 금지. 클릭 반응은 ISelectable, 배치는
   PlacementRequest로 참여. 스킬 타겟팅·병사 배치도 MouseManager 상태 추가로 구현.
   (Docs/Core/MouseManager.md)
2. **데이터 파이프라인**: 게임 수치는 CSV(`Assets/Resources/DataTables/`) → DataTableManager → SO
   패턴. 새 데이터 타입은 `XxxData`(POCO)+`XxxAsset`(SO)+`XxxTable` 템플릿을 따른다.
   Get 계열 null 반환 → 호출부 null 체크 필수. (Docs/Tools/DataTableManager.md)
3. **자원 흐름 고정** (GDD §4.2): 나무/철/식량 = 주민 배치 생산에서만 획득, 마나석 = 영토 확장·전투
   보상에서만. 우회 경로 신설 금지.
4. **공간 분리** (GDD §4.1/§6.2): 경영 공간 = 건물, 전투 공간 = 타워. 두 영토는 독립 관리 —
   한쪽 확장이 다른 쪽 상태에 의존 금지.
5. **낮/밤 전환 계약** (GDD §5, Build0 계획): 낮 시작=본진 회복, 낮→밤=주민 배치 기반 자원 정산,
   밤→낮=주민 배치 초기화+웨이브 증가. 페이즈에 반응하는 시스템은 전환 이벤트 훅 구조여야 한다.
6. **책임 경계** (MouseManager.md §2): 배치 판정=그리드/검증 시스템, 자원 차감=경영 시스템,
   정보 표시=UI. MouseManager는 선택 사실만 통지.
7. **문서-코드 동기화**: 시스템 구현·변경 PR은 해당 Docs/ 문서 갱신 포함 필수. (일치 여부 자체는
   설계 검증이 아님 — 갱신 포함 여부만 확인)
8. **저장소 배치** (CLAUDE.md): WIP는 `Assets/Personal/<이름>/`, `Assets/Imported/` 수정 금지.

## 5. 미합의 전역 계약 (합의 없는 변경·점유 = 최소 🟠)

- **레이어**: Selectable, Ground(MouseManager), Enemy(Tower.enemyLayerMask)가 각자 SerializeField로만
  존재. 프로젝트 레이어 규약 미확정. `TagManager.asset` 변경은 반드시 리뷰 대상.
- **좌표계**: MapBuilder는 battlespace 로컬 정수 그리드(MapSize=7), MouseManager/Combat은 월드 좌표.
  변환 유틸 없음.
- **네임스페이스**: `NorthLand.Combat`만 존재, 나머지 전역. asmdef 없음(전부 Assembly-CSharp).
- **매니저 수명주기**: static(DataTableManager) / DontDestroyOnLoad(MouseManager) / 씬 싱글톤
  (TowerInfoUI) 3종 공존. 부트스트랩 미결정.
- **에셋 로딩**: Resources.Load(DataTable)와 Addressables(Localization) 공존.
- **스탯 데이터 원본**: Combat의 TowerData/EnemyData(SO 직접 입력) vs DataTable CSV 파이프라인 —
  단일화 미결정 (WL-001).
- **용어 '웨이포인트'**: MapBuilder의 StageWaypoint(블록 경계 연결점) ≠ GDD §6.4 웨이포인트(병사
  배치 지점) (WL-009).
- **용어 '스테이지'**: MapBuilder의 블록 단위 ≠ GDD의 런 단위 스테이지 (WL-009).

## 6. 확립된 컨벤션 (일관성 판단 기준)

- MonoBehaviour는 얇게(진입점), 로직은 생성자 주입 순수 C# 클래스로
- 실패 처리: `bool Try~(out/결과 객체)` + Debug.LogError 한국어 메시지 + null 반환(호출부 체크)
- `[SerializeField] private` 필드 기본, 프로퍼티는 expression-bodied (접두 `_camelCase` vs
  `camelCase` 혼재 — 통일 미결정)
- CSV POCO는 PascalCase 프로퍼티(CsvHelper), SO는 CreateAssetMenu
- 테스트: XxxTest.cs MonoBehaviour + 개인 테스트 씬 Play 확인 (유닛 테스트 없음)
- 커밋: `Feat|Fix: 한국어 요약 #이슈번호`
