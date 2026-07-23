# SystemMap — 시스템 지도와 통합 계약 (PR 리뷰 필수 입력)

> **목적**: PR 리뷰 시 "이 변경이 누구의 어떤 시스템과 만나는가"를 판단하는 기준 문서.
> **갱신 규칙**: 시스템의 공개 API·계약이 바뀌는 PR은 이 문서를 **같은 PR에서** 갱신한다.
> 자동 리뷰 워크플로우(`.github/workflows/pr-review.yml`)가 매 리뷰마다 이 문서를 읽는다 —
> 낡은 지도는 리뷰 품질을 직접 해친다.

## 1. 시스템 및 소유자

| 시스템                                      | 소유자     | 경로                                                                 | 상태                                                                                                                                                                    |
| ------------------------------------------- | ---------- | -------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| DataTable (CSV→static 레지스트리→SO)        | muchan     | `Assets/Scripts/Data`                                             | Resource, Building, Tower, Enemy 4종 구현. Tower/Enemy는 Combat(`Tower.cs`/`Enemy.cs`)이 `TowerAsset`/`EnemyAsset`을 직접 소비하도록 이관 완료(PR#80) — 잔여 종류 값 채움 + Soldier 이관은 진행 중(WL-001, 부분 착수). Territory/Reward 확장 예정. **Skill(#103)은 CSV 파이프라인을 쓰지 않기로 확정** — 밸런싱 수치 미정 + 스킬 1~2개뿐이라 과설계로 판단, `PlayerSkill` 시스템 행 참고                                |
| Combat (타워/몬스터 공격·데미지)            | SUNGSOO    | `Assets/Scripts/CombatSystem` | 공격/데미지 코어만. 이동·사망처리·투사체 없음. HP 조회 공개 API(`CurrentHp`/`MaxHp`/`OnHpChanged`) + `PlayerBase` 씬 싱글톤(`Instance`/`OnBaseSpawned`) 추가(#100, HP UI 연동용). `Tower.cs`에 PlayerSkill(#103, muchan)이 버프 배율 필드(`damageMultiplier`/`attackSpeedMultiplier`)와 자가 등록 정적 리스트 `Tower.Active`를 추가함 — 기존 공격 로직·필드는 무수정 |
| BattleMapBuilder (절차적 전투 맵)           | SUNJIN     | `Assets/Scripts/CombatSpace/MapBuilder`                          | 7×7 블록 경로 생성 구현. 싸이클 버그 해결이 다음 빌드 목표                                                                                                              |
| MouseManager (입력/선택/배치)               | n0wst4ndup | `Assets/Scripts/GameManager/MouseManager`                            | 3상태 머신 구현(Idle/Placement/SkillTargeting, #103에서 SkillTargeting 추가). Snap 항등·CanPlaceAt 항상 true (TODO). 스킬 타겟팅은 전투 타일 전체 허용(`CombatMapTileView` 유무 질의, 도로 전용 제한 제거)                                                                                                                  |
| PlayerSkill (플레이어 스킬, #103)           | muchan     | `Assets/Scripts/Skill`                                                | 클릭 시전 감전 스킬(기본 스킬 1종). 밤 게이팅(`Tower.cs`와 동일하게 `DayNightManager.CurrentPhase` 직접 폴링)·쿨다운·범위 데미지(`IDamageable`/`DamageInfo` 재사용, 새 데미지 경로 없음). 수치는 CSV가 아니라 `SkillManager` 인스펙터 직접 입력(WL-015와 같은 축). **버프 스킬 구현 완료**(2번째 스킬, `BuffSkillManager`) — 타겟팅 없이 클릭 즉시 발동, `Tower.Active` 순회해 씬의 모든 Tower에 공격력/공격속도 배율을 일정 시간 부여(`Tower.ApplyBuff`). AuraTower(Magic 타입)는 `AttackFields` 자체가 없어 버프 대상 아님. 보상 기반 특수효과 업그레이드(감전→퍼지는→다단히트)는 미착수 — WL-043(3택1 보상 구조 미착수)이 먼저 해결돼야 착수 가능 |
| Localization                                | n0wst4ndup | `Assets/Scripts/Localization/LocalizationHelper.cs`, `Assets/Localization/*`(String Table 컬렉션), `Assets/Scripts/Test/LocalizationTest.cs` | String Table 4종(`NorthLand_default`/`NorthLand_buildings`/`NorthLand_Enemies`/`NorthLand_Towers`, ko-KR/en-US/ja-JP) 구축. Building/Enemy/Resource/Tower CSV 표시 문자열은 키로 이관 완료(WL-013 해소, PR#126 — 신규 `poison_tower` 행 포함). `LocalizationHelper`(static 동기 pull 헬퍼) 신설 — 호버 툴팁 등 '호출 시점 1회' 풀 경로 전용, 지속형 표시는 `LocalizeStringEvent`/`LocalizedString.StringChanged` 사용. 전투 공간(TowerInfoUI) 표시 배선은 후속(#102) |
| DayNightManager (낮/밤 상태·전환 이벤트 훅) | muchan     | `Assets/Scripts/DayNight`                                    | 상태 관리 + 전환 이벤트 훅 구현. 자원 정산/주민 배치 초기화는 `Management(Resource)`가 구현(#66), 본진 회복은 미구현(소유 시스템 대기). 밤→낮 트리거는 임시 UI(`NightActionPanelView`의 "웨이브 성공" 버튼, #66)가 `EndNight()` 직접 호출(웨이브 클리어 로직으로 교체 예정, WL-018) |
| DayNightLighting (낮/밤 전환 조명·스카이박스 연출, #7) | muchan     | `Assets/Scripts/DayNight`                                    | `OnDayToNight`/`OnNightToDay` 구독해 Directional Light·Ambient(Trilight)·Skybox를 즉시 전환(스냅). 부드러운 Lerp 전환은 미구현 — 밤 종료 자동화의 UniTask 전환 작업과 함께 후속 예정 |
| Management(Resource) (자원 지갑·생산처)     | n0wst4ndup | `Assets/Scripts/ManagementSpace`                              | 지갑·생산처(#42) + 경영 패널 UI·DayNightManager 낮/밤 루프 연동(#43, #66). 정산+주민 배치 초기화=OnNightToDay(정산 먼저). **밤→낮 전환은 이제 밤 전용 임시 UI(`NightActionPanelView`)의 "웨이브 성공" 버튼이 트리거(WL-018)** — 경영 패널(`RequestAdvancePhase`)은 낮→밤(`EndDay`)만 담당. 주민 수는 placeholder(주민 시스템 부재). 소비처·마나석 생산 후속. **✅ 확장 자원 라인 구현(#166)**: 미개척 영지(영토 해금) = 특수 자원(금/루비/사파이어/다이아) **매일 자동 수급** — `HandleNightToDay`가 Owned 노드에서 `SupplyDaily`만큼 `Add`(주민 배치 무관). 패널은 **고정 8행**(기본3+마나+특수4, 동적 등록 아님): 특수/마나는 +/- 숨김, 특수는 "+n"(일일 수급)·**미개방 시 회색**·활성 우선 재정렬, 마나 "+n"=`ManaPerWaveClear`. `ProductionLineView`에 Villager/Supply/Mana 모드. **지갑(보유량) 표기를 탑 바 → 각 행의 지갑 칸(`_balanceText`→ProdRow Wallet)으로 이관**(#166): 탑 바 `Wood/Iron/Food/Mana_hud` 비활성화, 주민 풀·페이즈만 탑 바 유지. **🔀 잔여 방향**: ②생산 건물 3종 업그레이드(#139 구현됨), ③탑 바 HUD 오브젝트 완전 삭제는 후속 |
| TerritoryGraph (경영 영토 확장)             | n0wst4ndup (View 비주얼: muchan) | `Assets/Scripts/ManagementSpace/Territory`                    | 그래프 생성(Delaunay+프루닝)·클레임(`ISelectable`)·점진 공개·호버 하이라이트(`IHoverable`) 구현, `GameScene`에 씬 통합 완료(#18, #67). 하루 1회 확장 게이팅(`HasExpandedToday`, `DayNightManager.OnDayStart` 연동)도 #67에서 추가. **노드 비주얼 에셋 적용(#127, PR#128, muchan)**: `TerritoryNodeStateVisual`(상태→비주얼 스왑: Selectable=절차 생성 소용돌이 `VortexVisual`, Owned=산 에셋, 본진=씬 지형)+확보 연출(UniTask). `GameScene`의 `TerritoryGraphView._nodePrefab`=`TerritoryNodeV2`(간격 튜닝 세트와 결합 — WL-059). 구형 프리팹은 기존 색상 경로 폴백. **엣지 배 연출(#93, muchan)**: 엣지 선(LineRenderer)을 `SweetBoat` 랜덤 1척이 왕복하는 `TerritoryEdgeShip` 연출로 교체(선은 `_drawEdgeLines` 기본 꺼짐), 양끝이 모두 `Owned`일 때만 표시(`TerritoryGraph.IsOwned`). **🔀 영토 = 미개척 영지 자원 재설계 완료(#166)**: 효과 SO 계층(`TerritoryEffect`/`Grant`/`GainResident`/`ProductionMultiplier`/`Context`)을 **폐기·삭제**하고 `TerritoryDefinition`을 "자원 영지 정의"(`Kind`/`IslandPrefab`/`Min·MaxDaily`)로 리셰이프. 노드는 주입 시점에 `DailyYield`를 [Min,Max]에서 1회 롤(`TerritoryNode.DailyYield`, 시드 결정성). **확보 즉시 지급은 없고**, 매일 정산 시 `ManagementController`가 Owned 노드에서 자동 수급(GDD §3.2·§5.3). 섬 프리팹도 SO 소유로 이관(`TerritoryNodeStateVisual._mountainPrefabs`는 폴백만). `OnNodeClaimed` 훅은 뷰 확보 연출용으로 잔존하나 자원 적용엔 더 이상 안 씀(WL-030 종결) |

## 2. 공개 API (다른 시스템이 소비해도 되는 것)

- `DataTableManager.Get<T>(string id)` — static. **null 반환 가능 → 호출부 null 체크 필수**
- `ResourceTable.Get(string id)` — null 반환 가능
- `BuildingTable.Get(string id)` — null 반환 가능
- `TowerTable.Get(string id)` — null 반환 가능. `TowerAsset`은 Combat의 `Tower.cs`가 직접 소비함
  (PR#80으로 이관 완료) — 잔여 타워 종류의 값 채움은 WL-001 참고
- `EnemyTable.Get(string id)` — null 반환 가능. **스탯(체력/이동속도/공격력/사거리/공격주기)은
  CSV/`EnemyData`에 없음** — `EnemyAsset`(SO)의 `EnemyType`별 필드 그룹(`MeleeFields`/`RangedFields`/
  `BossFields`, `TowerAsset`의 `TowerType`별 필드 그룹과 동일 패턴)에만 존재. 이슈#26 원 스펙(스탯
  CSV 컬럼 요구)과 다른 선택이며 WL-027로 추적 중 — 스탯이 필요한 소비처(#14/#15/#16)는
  `EnemyTable.Get(id)`가 아니라 `EnemyAsset` 조회 경로를 써야 함. `EnemyAsset`도 `TowerAsset`과
  동일하게 Combat의 `Enemy.cs`가 직접 소비함(PR#80으로 이관 완료, 옛 Combat 자체 `EnemyData`는
  삭제됨) — 잔여 종류 값 채움은 WL-001 참고. `BossFields.BehaviorTree`는 실제 BT 에셋 타입 미정 상태의 placeholder 필드
- `ResourceAsset.Data` / `BuildingAsset.Data` / `TowerAsset.Data` / `EnemyAsset.Data` — **호출부가
  Start()에서 직접 채우는 규약** (저장 안 됨)
- `BuildingInfoUI.Instance.ShowInfo(string)` / `HideInfo()` — 경영 공간 전용 정보 패널. `TowerInfoUI`와
  동일 구조의 별도 씬 싱글톤 (공간 분리 계약상 Combat의 `TowerInfoUI`와 공유하지 않음)
- `LocalizationHelper.Get(table, entry)` — static 동기 조회(현재 로케일). **풀(pull) 경로 전용** —
  호버 툴팁 등 호출 시점 1회 값이 필요한 경우만. 지속형 표시(상세 패널 등, 로케일 변경 시 자동 갱신
  필요)는 `LocalizeStringEvent`/`LocalizedString.StringChanged`를 쓴다. 테이블명 상수
  `k_DefaultTable`/`k_BuildingsTable`/`k_TowersTable`/`k_EnemiesTable` 제공 — **컬렉션명은 대소문자
  구분**이라 실제 컬렉션명(`NorthLand_Towers`/`NorthLand_Enemies`, 대문자)과 정확히 일치해야 함(PR#126에서 정정, WL-060)
- `IDamageable { Faction, IsDead, TakeDamage(DamageInfo) }`, `IAttacker`, `DamageInfo`,
  `Faction { Player, Enemy }` — namespace `NorthLand.Combat`
- `Enemy.CurrentHp` / `MaxHp` / `event Action<float,float> OnHpChanged`, `PlayerBase.CurrentHp` /
  `MaxHp` / `event OnHpChanged` / `static Instance` / `static event Action<PlayerBase> OnBaseSpawned`
  — HP UI(`Assets/Scripts/UI/HealthUI`, #100)가 구독하는 공개 계약. `PlayerBase.Instance`는 성문
  (BaseGate) 런타임 스폰 시점(`MonsterSpawn.UpdateGate`)에 설정됨 — `TowerInfoUI`/`DayNightManager`와
  동일한 씬 싱글톤 계보
- `ResourceWallet` (경영 자원 상태 저장소, 순수 C#) — `Get(ResourceKind)`, `CanAfford(kind, amount)`,
  `Add(kind, amount)`, `bool TrySpend(kind, amount)`(부족 시 false+로그, 차감 안 함),
  `event Action<ResourceKind,int> OnChanged`(종류, 변경 후 값). 자원 획득/차감은 이 창구로만(팀 계약 #3·#6)
- `ResourceProductionSource` (건물 생산 단위, 순수 C#) — `int CalculateAmount(villagerCount, amountPerVillager, mult)`(순수),
  `int Produce(villagerCount, amountPerVillager, mult)`(정산: 지갑에 Add, 넣은 양 반환), `static bool TryCreate(BuildingAsset, ResourceWallet, out)`(OutputResource만 캡처).
  **주민 수·주민당 생산량을 인자로 받는 무상태 심**(주민당량은 건물 업그레이드로 가변 — #139; readonly 필드 제거). 정산 트리거는 이제 `ManagementController`가 DayNightManager 이벤트로 호출. `OutputResource.Data.Kind`로 지갑 키 해석(→ Data 채움 규약 의존)
- `ManagementController` (경영 로직/모델, MonoBehaviour) — 지갑·생산처·주민 배치·업그레이드 상태 소유. `AssignVillager(int)`/
  `UnassignVillager(int)`, `RequestAdvancePhase()`(낮→밤 `EndDay()`·잉여 게이트 전용 — **밤→낮 `EndNight()`은 더 이상
  이 메서드가 호출하지 않음, #66. 밤 전용 임시 UI `NightActionPanelView`의 "웨이브 성공" 버튼이 직접 호출, WL-018**),
  **건물 업그레이드**(#139): `bool TryUpgrade(int)`·`bool CanUpgrade(int)`·`int LineLevel/LineMaxLevel/LineAmountPerVillager(int)`·
  `IReadOnlyList<ResourceCost> LineUpgradeCost(int)` — 낮 전용, 수치는 `BuildingAsset.Production.UpgradeLevels`(SO),
  **소비 게이트웨이** `bool CanAfford/TrySpend(IReadOnlyList<ResourceCost>)`(소비처는 지갑 직접 접근 대신 경유, 원자 차감 — WL-017),
  질의 `ResourceCount`/`LineCount`/`LineKind`/`LineExpectedProduction`/`AssignedTotal`/`IsDay`/`CanAdvancePhase`, `event OnChanged`(뷰 갱신).
  UI(`ManagementPanelView`/`ProductionLineView`)는 이 컨트롤러만 구독·호출 — UI 아트 교체 시 뷰 참조만 재연결
- `MouseManager.Instance.BeginPlacement(PlacementRequest)` / `CancelPlacement()` / `event OnSelectionChanged`
- `MouseManager.Instance.PointerPosition`(포인터 화면 좌표 — Mouse.current 직접 폴링 대신 이걸 쓴다) /
  `event OnHoverChanged(IHoverable)`(커서 밑 호버 대상, 없으면 null. Idle에서만 통지)
- `ISelectable { OnSelected(), OnDeselected() }`,
  `PlacementRequest { GhostPrefab, Snap(RaycastHit→pos), CanPlaceAt(RaycastHit), OnConfirmed(RaycastHit,pos), OnEnded, KeepPlacingAfterConfirm }` — **히트 인지형**: 스냅/검증/확정을 요청 측이 소유(MouseManager는 그리드 규칙 무지), `OnEnded`로 취소/확정 시 프리뷰 정리(PR#81)
- `MouseManager.Instance.BeginSkillTargeting(SkillTargetRequest)` / `CancelSkillTargeting()`(#103) —
  `SkillTargetRequest { GhostPrefab, OnConfirmed(Vector3), OnEnded }`. `PlacementRequest`의 경량 버전(`Snap`/`CanPlaceAt` 없음).
  **전투 타일 전체 허용**: `_placementMask` 히트에서 `CombatMapTileView` 유무로 전투 타일 여부만 판정(도로 전용 제한·유효/무효 색 제거),
  전투 타일 밖에선 인디케이터 숨김. **전투 타일 위 좌클릭**이면 `OnConfirmed(hit.point)`로 확정(타일 게이팅은 MouseManager 소유 — 요청 타입엔 `CanPlaceAt` 훅 없음)
- `SkillManager.Instance` — **null 반환 가능(씬에 없으면) → 호출부 null 체크 필수**(#103).
  `CastAt(Vector3)`(범위 내 적에게 데미지, 밤+쿨다운 게이팅 통과 못하면 false), `CanCast()`,
  `IsReady`, `CooldownRemaining01`(0~1, UI 바인딩용), `Radius`
- `BuffSkillManager.Instance` — **null 반환 가능 → 호출부 null 체크 필수**(#103). `Activate()`(타겟팅 없이
  즉시 발동, 밤+쿨다운 게이팅 통과 못하면 false), `CanCast()`, `IsReady`, `CooldownRemaining01`
- `Tower.Active`(`static List<Tower>`, `NorthLand.Combat`) — 씬에 활성화된 모든 Tower가 `OnEnable`/`OnDisable`로
  자가 등록/해제(#103). `FindObjectsByType<Tower>()` 대체용 — 소비처가 씬을 훑지 않고 순회만 하면 됨
- `Tower.ApplyBuff(float damageMul, float attackSpeedMul, float duration)` — 지속시간 동안 공격력/공격속도
  배율 적용 후 자동 원복(UniTask, `AuraTower.AuraLoop`와 동일한 `CancellationTokenSource` 패턴). 공유 `TowerAsset`
  값은 건드리지 않음 — 인스턴스별 런타임 배율만 조작
- `IHoverable { TooltipContent? GetTooltipContent(), void OnHoverEnter(), void OnHoverExit() }` — 호버 시 툴팁 내용을 pull 공급(호버 시점마다 호출 → 동적 값 가능, `null`이면 툴팁 없음)
  + 호버 진입/이탈 훅(하이라이트 등 연출, `MouseManager.SetHover`가 대상 전환 시 호출, #67)
- `TooltipUI.Instance.Show(TooltipContent)` / `Hide()` — 커서 추적 범용 툴팁 뷰(#38). **임시 싱글톤(UIManager 흡수 예정)**,
  `TowerInfoUI`/`BuildingInfoUI`와 동일 계보. `OnHoverChanged`를 자체 구독. `Assets/Scripts/GameManager/MouseHover`
- `TooltipContent { Header, Body, HeaderColor, BackgroundColor }` — 구체 개념 무지한 표시 데이터. 건물·버프 등 공급자가 채움
- `BuildingTooltipSource`(건물용 `IHoverable` 어댑터, `BuildingAsset`/`BuildingData` **읽기 전용** 소비) +
  `BuildingTooltipPalette`(`BuildingType`→색 SO). 클릭 선택 `BuildingInfo`와 **역할 분리**(호버=요약 툴팁, 클릭=기능 패널)
- `DayNightManager.Instance` — **null 반환 가능(씬에 없으면) → 호출부 null 체크 필수**.
  `CurrentPhase` / `WaveCount` / `EndDay()` / `EndNight()` / `event OnDayStart, OnDayToNight, OnNightToDay`.
  `OnDayStart`는 1일차 부트스트랩 포함 매 낮 시작마다 발생, `OnNightToDay`는 밤을 거친 전환에서만 발생(웨이브 종료 의미) — 구독 시 구분해서 사용할 것.
  `EndNight()`은 이제 `MonsterSpawn`이 웨이브 클리어(스폰 완료 후 생존 0) 시 자동 호출(#17)하며, 밤 전용 임시 UI(`NightActionPanelView`의 "웨이브 성공" 버튼, #66) 수동 호출도 병존한다. 단 클리어가 아직 처치가 아닌 본진 도달-디스폰 기준(처치 기반은 Enemy 병합 후 — WL-038); 실패/보스 판정 연동·임시 버튼 제거는 WL-018 잔여
- `TerritoryController.Instance` — 씬 싱글톤(`DayNightManager`와 동일 패턴, `DontDestroyOnLoad` 없음).
  `Graph`(읽기 전용 질의), `bool TryClaim(int nodeId)`(유일한 변경 진입점 — 구조 불변식은 `Graph`,
  하루 1회 게이팅 정책은 이 레이어), `bool HasExpandedToday`(오늘 확장 완료 여부, `OnDayStart`마다
  초기화), `event OnChanged`(그래프 상태 또는 `HasExpandedToday` 변경 시 발행)
- `TerritoryGraph` (순수 C# 모델) — `Nodes`, `Frontier`, `OwnedCount`, `GetNode(id)`(null 반환 가능),
  `bool IsRevealed(id)`(Owned+Selectable 공개 판정), `bool IsOwned(id)`(Owned 전용 — 엣지 배 연출 게이팅용, #93), `bool TryClaim(id)`(구조 불변식만 검사 — Selectable만 확보 가능),
  `event OnNodeClaimed`(효과 적용 훅 — `ManagementController`가 구독해 효과 Apply, WL-030 해소), `event OnChanged`
- **미개척 영지 자원 SO**(`ManagementSpace/Territory/TerritoryDefinition.cs`, TerritoryGraph.md §5, #166) —
  `TerritoryDefinition`(SO): `ResourceKind Kind`(금/루비/사파이어/다이아), `GameObject IslandPrefab`(확보 시 섬),
  `int MinDaily`/`MaxDaily`, `int RollDailyYield(System.Random)`(주입 시 [Min,Max] 1회 롤), 표시명/설명 키(`NorthLand_Territories`).
  수치는 **SO에 authored(CSV 아님, 팀 결정)**. `TerritoryNode.Definition`(SO ref)+`TerritoryNode.DailyYield`(롤 결과)에 주입되며,
  배정·롤은 `TerritoryController`가 생성 시 `_seed`로 수행(SO 4종<노드라 자원 중복 정상, 시드 결정성).
  **수급**: `ManagementController.HandleNightToDay`가 매 정산 시 `Graph.Nodes` 중 Owned+Definition 노드를 순회해
  `ResourceWallet.Add(Definition.Kind, DailyYield)` — **확보 즉시 지급 없음, 주민 배치 무관**(GDD §3.2 자동 수급).
  ⚠ 종전 효과 SO 계층(`TerritoryEffect`/`GrantResourceEffect`/`GainResidentEffect`/`ProductionMultiplierEffect`/`TerritoryEffectContext`)은 **삭제됨**.
  `ProductionModifiers`는 잔존하나 생산자가 없어 항상 ×1(기본 라인 정산·예상치 호출부 무변경)
- `ManagementController.SupplyDaily(ResourceKind)` — Owned 그 종류 영지들의 `DailyYield` 합(없으면 0). 패널 특수 자원 row의 "+n"·활성 판정용.
  `ManagementController.ManaPerWaveClear`(int) — 마나 row "+n" 미리보기용(웨이브 클리어 고정 마나)
- `StageRoadTracker.RoadWorldPoints` — ⚠️ HashSet(순서 없음). **이동 경로로 사용 불가**
- MapBuilder의 **순서 있는 경로·스폰 지점·최종 목표 좌표는 아직 공개 API가 없음** (WL-003)

## 3. 접점 매트릭스 (왼쪽 시스템을 건드리는 PR은 오른쪽 항목을 실제 코드로 대조)

| 접점                                     | 확인할 것                                                                                                                                                                                                                  |
| ---------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Combat ↔ BattleMapBuilder                | 몬스터 이동 경로(순서 있는 경로 API 부재 — WL-003), 레이어(WL-005), 좌표계(battlespace 로컬 vs 월드 — WL-007)                                                                                                              |
| Combat ↔ DataTable                       | 스탯 데이터 원본(CSV 파이프라인 vs 손입력 SO — WL-001)                                                                                                                                                                     |
| MouseManager ↔ BattleMapBuilder          | 그리드 스냅, CanPlaceAt·타일 종류 질의 API(WL-004), 좌표계(WL-007)                                                                                                                                                         |
| MouseManager ↔ Combat                    | 타워 배치(PlacementRequest→Tower 프리팹), 선택(ISelectable), TowerInfoUI 데이터 연동(WL-011)                                                                                                                               |
| DataTable ↔ Localization                 | 표시 문자열 소유권: Building/Enemy/Resource/Tower CSV 표시 문자열을 String Table 키로 이관 완료(WL-013 해소, PR#126). 잔여: 전투 공간(TowerInfoUI) 키 조회 배선 후속(#102). 테이블명 상수 대소문자 정합(WL-060)                                                                                                                                                          |
| Management(Resource) ↔ DataTable         | `ResourceKind`(지갑 키)·`BuildingAsset.ProductionFields`(생산처 입력)·`ResourceAsset.Data`(정산 시 `Kind` 해석, 호출부 `Start()` 채움 규약) 의존 — muchan이 이 구조 바꾸면 자원 시스템 깨짐                                |
| Management(Resource) ↔ DayNightManager   | 정산+주민 초기화=`OnNightToDay` 구독(정산 먼저), 낮→밤 전환=`ManagementController.RequestAdvancePhase()`가 `EndDay()` 호출. **밤→낮(`EndNight`)은 이제 `NightActionPanelView`의 "웨이브 성공" 버튼이 임시 트리거 — 밤 종료 주체(Combat 웨이브 클리어 등)로 책임 이관 필요(WL-018)**. 주민 수는 여전히 placeholder(주민 시스템 부재)                |
| BattleMapBuilder/Monster ↔ DayNightManager | 밤 시작(`OnDayToNight`)에 `StageBuilder`가 구독 → 다음 스테이지 생성(전투영역 확장) + `MonsterSpawn.StartRound`로 몬스터 스폰(`currentMapCount > 1`, #17). `MonsterSpawn`은 낮이면 스폰 스킵(경고 로그). 웨이브 클리어(스폰 완료 후 생존 0) 시 `MonsterSpawn`이 `EndNight()` 호출로 낮 복귀(#17) — 단 본진 도달-디스폰 기준(처치 기반은 Enemy 병합 후 WL-038); 실패/보스 판정·임시 버튼 제거는 WL-018 잔여 |
| Management(Resource) ↔ 주민(미존재)      | 주민 수 입력 심 — 현재 `_maxVillagers` placeholder + 패널 +/-. 주민 시스템 생기면 출처 이관                                                                                                                                |
| Management(Resource) ↔ Territory         | `TerritoryController.HasExpandedToday`(하루 1회 확장 완료 여부, `OnDayStart`마다 초기화)가 `ManagementController.CanAssignVillagers`를 게이팅 — 확장 전엔 `AssignVillager`/`UnassignVillager` 불가(이슈 #67, GDD §6.1). `ManagementController`가 `TerritoryController.OnChanged` 구독해 확장/낮 시작 시 패널 즉시 갱신(`ProductionLineView`의 `+`/`-` `interactable`도 함께 반영). `TerritoryController`가 씬에 없으면(null) 게이트 없이 배치 허용(permissive, WL-002와 동일 완화 패턴). **자원 수급(#166)**: `ManagementController.HandleNightToDay`가 매 정산 시 `Graph.Nodes`의 Owned+Definition 노드를 순회해 `ResourceWallet.Add(Definition.Kind, DailyYield)`로 **매일 자동 수급**(확보 즉시 지급·주민 배치 무관, GDD §3.2). 종전 `OnNodeClaimed` 즉시 효과 적용 경로는 제거됨 — muchan이 `TerritoryNode.Definition`/`DailyYield`·`Graph.Nodes` 구조를 바꾸면 수급 정산이 깨짐 |
| DataTable(Building) ↔ MouseManager       | `BuildingInfo`가 `ISelectable` 구현 + `BuildingAsset` 보유 — 선택 시 `BuildingInfoUI` 직접 호출(이벤트 미구독, WL-011과 동일 패턴). `BuildingTooltipSource`(#38)가 `IHoverable` 구현 + `BuildingAsset`/`BuildingData`/`BuildingType`을 **읽기 전용** 소비(muchan 구조 바뀌면 툴팁 깨짐 — 자체 `DataTableManager.Get` 조회, Data 채움 규약 의존). `MouseManager`가 씬에 없으면 조용히 무반응(WL-002) — 씬마다 배치·`_camera` 재할당 필요 |
| MouseManager ↔ TerritoryGraph            | `TerritoryNodeView`가 `ISelectable`(클릭=즉시 `TerritoryController.TryClaim`) + `IHoverable`(호버 하이라이트 — 신형 프리팹(`TerritoryNodeV2`)은 `TerritoryNodeStateVisual`에 위임해 소용돌이 밝기/가속·소진 시 회색, 구형 프리팹은 기존 색 변경 폴백. `GetTooltipContent()`는 노드의 `TerritoryDefinition` 이름·설명을 `LocalizationHelper`(`NorthLand_Territories` 테이블)로 pull해 `TooltipUI`에 공급 — 정의 없는 노드(본진)는 `null`) 둘 다 구현 — 같은 콜라이더에 두 인터페이스 공존이 이미 지원되는 경로임을 실증(#67, `BuildingInfo`+`BuildingTooltipSource` 조합과 동일 패턴). Layer 6(`Selectable`) 배정 확인됨(WL-005 해소). **클릭 판정은 노드 루트 `SphereCollider` 전용** — `MouseManager`가 `hit.collider.TryGetComponent`로 부모 미탐색이므로 산 에셋의 자식 콜라이더는 스폰 시 전부 비활성(#127). 엣지 배 연출(#93)도 인스턴스 배의 `MeshCollider`를 스폰 시 제거해 선택 레이캐스트 간섭을 차단(동일 취지) |
| PlayerSkill ↔ MouseManager               | 스킬 버튼 클릭 → `BeginSkillTargeting(SkillTargetRequest)` → **전투 타일 위이면**(`CombatMapTileView` 존재) 확정, `OnConfirmed(Vector3)`로 `SkillManager.CastAt` 호출(#103). 인디케이터는 전투 타일 밖 숨김(유효/무효 색 없음). `PlacementRequest`와 별개 타입 — 그리드 개념 없음                                                                                                            |
| MouseManager ↔ CombatSpace(맵)           | 스킬 타겟팅이 히트 타일의 `CombatMapTileView` 유무로 전투 타일 여부 판정(#103 후속, 도로 전용 제한 제거). MouseManager→CombatSpace 단방향 읽기(입력 매니저가 전투 공간 타일 데이터에 의존 — 지켜볼 커플링)                                            |
| PlayerSkill ↔ Combat                     | (감전) 새 데미지 파이프라인 없이 `IDamageable`/`DamageInfo`/`Faction`을 그대로 소비(`Tower.FindTarget()`/`Projectile.ApplyArea()`와 동일한 `OverlapSphereNonAlloc`+Faction 필터링 패턴). `DamageInfo.Source`는 스킬 시전 시 `null`(IAttacker 개체가 아님 — 현재 아무도 역참조 안 해 안전). (버프) `Tower.Active` 순회 + `Tower.ApplyBuff` 호출 — `Tower.cs`에 직접 필드를 추가한 형태라 SUNGSOO의 Combat 코드와 결합도가 감전보다 높음(WL 후보로 지켜볼 것) |
| 모든 시스템 ↔ 전역 설정                  | 레이어/태그(`ProjectSettings/TagManager.asset` — WL-005), URP 설정(`Assets/Settings`), 패키지(`Packages/manifest.json`)                                                                                                    |

## 4. 팀 계약 (위반 = 🔴 후보)

1. **입력 단일 창구**: 포인터/키보드 입력은 MouseManager만 읽는다. 게임플레이 코드의
   `Mouse.current`/`Keyboard.current` 직접 폴링 금지. 클릭 반응은 ISelectable, 배치는
   PlacementRequest로 참여. 스킬 타겟팅·병사 배치도 MouseManager 상태 추가로 구현.
   (Docs/Core/MouseManager.md)
2. **데이터 파이프라인**: 게임 수치는 CSV(`Assets/Resources/DataTables/`) → DataTableManager → SO
   패턴. 새 데이터 타입은 `XxxData`(POCO)+`XxxAsset`(SO)+`XxxTable` 템플릿을 따른다.
   Get 계열 null 반환 → 호출부 null 체크 필수. (Docs/Tools/DataTableManager.md)
3. **자원 흐름** (GDD §3.2): 기본 자원(나무/철/식량) = 주민 배치 생산 **또는 영토 확장 보상**, 마나석 =
   영토 확장·전투 보상에서만.
   - **방향 전환(GDD v0.3)**: **미개척 영지 자원**(영토 해금)은 주민 배치 없이 **매일 정산마다 일정량이 자동
     수급**된다(영토 확장 보상의 일종) — 영토 해금이라는 정당한 원천이므로 계약 위반 아님. (직전 '식량 소모 →
     확장 자원 변환' 모델은 폐기, WL-042 참고.) 그 밖의 우회 경로(마나석→기본 자원 교환 건물 등)는 여전히 금지/미결.
4. **공간 분리** (GDD §4.1/§6.2): 경영 공간 = 건물, 전투 공간 = 타워. 두 영토는 독립 관리 —
   한쪽 확장이 다른 쪽 상태에 의존 금지.
5. **낮/밤 전환 계약** (GDD §5, Build0 계획): 낮 시작=본진 회복, 밤 시작(`OnDayToNight`)=전투 스테이지 확장+몬스터 스폰(#17), 밤→낮=주민 배치 기반 자원 정산(먼저)+
   주민 배치 초기화(그 다음)+웨이브 증가(모두 `OnNightToDay` 시점, #66). 페이즈에 반응하는 시스템은
   전환 이벤트 훅 구조여야 한다. (Docs/Core/DayNightManager.md)
6. **책임 경계** (MouseManager.md §2): 배치 판정=그리드/검증 시스템, 자원 차감=경영 시스템,
   정보 표시=UI. MouseManager는 선택 사실만 통지.
7. **문서-코드 동기화**: 시스템 구현·변경 PR은 해당 Docs/ 문서 갱신 포함 필수. (일치 여부 자체는
   설계 검증이 아님 — 갱신 포함 여부만 확인)
8. **저장소 배치** (CLAUDE.md): 스크립트 정본은 `Assets/Scripts/`(공간/시스템 폴더), 씬 등 비-스크립트 WIP는 `Assets/Personal/<이름>/`, `Assets/Imported/` 수정 금지.
   - **리뷰어 주석(Imported 사각지대)**: 씬/프리팹이 참조하는 유료·벤더 에셋(건물 프리팹, BaseGate 등)은 `Assets/Imported/`(중첩 git repo)에 상주할 수 있고 자동 리뷰 봇은 이를 읽지 못한다. 메인 repo diff에 `.prefab`이 없다고 해서 "프리팹 미생성/이슈 미충족"으로 단정하지 말 것 — 유료 에셋을 팀 공용 Imported 공간에만 두는 것이 정상 배치이며, 필요 시 작성자에게 확인한다(WL-040 참고, #92 건물 프리팹이 이 사각지대의 실제 오탐 사례).
   - **리뷰어 주석(죽은 사본)**: `Assets/Personal/SUNGSOO/Font/`는 폰트가 TMP 정본으로 이관되며 더 이상 참조되지 않는 죽은 사본이다 — 이 경로의 폰트 아틀라스 churn을 WL-041 재발로 보고하지 말 것(WL-041 참고, 삭제 대기 중).

## 5. 미합의 전역 계약 (합의 없는 변경·점유 = 최소 🟠)

- **레이어**: Enemy(7)/Soldier(8)/PlayerBase(9)가 `TagManager.asset`에 등재 완료(PR#80, WL-005 해소).
  단 각 스크립트(Tower/Soldier/Enemy)의 LayerMask vs Tag 방식 최종 확정은 TODO(TBD)로 남음.
  `TagManager.asset` 변경은 반드시 리뷰 대상.
- **좌표계**: MapBuilder는 battlespace 로컬 정수 그리드(MapSize=7), MouseManager/Combat은 월드 좌표.
  변환 유틸 없음.
- **네임스페이스**: `NorthLand.Combat`만 존재, 나머지 전역. asmdef 없음(전부 Assembly-CSharp).
- **매니저 수명주기**: static(DataTableManager) / DontDestroyOnLoad(MouseManager) / 씬 싱글톤
  (TowerInfoUI) 3종 공존. 부트스트랩 미결정. DayNightManager는 씬 싱글톤(DontDestroyOnLoad 없음)
  채택 — 경영/전투 공간이 한 씬에 공존해 씬 전환에 걸쳐 상태를 유지할 이유가 없다는 판단(WL-002 참고 사례).
- **에셋 로딩**: Resources.Load(DataTable)와 Addressables(Localization) 공존.
- **스탯 데이터 원본**: Combat의 `Tower.cs`/`Enemy.cs`가 `TowerAsset`/`EnemyAsset`(CSV 기반, 1절
  DataTable 상태 참고)을 직접 소비하도록 PR#80에서 이관 완료 — 옛 Combat 자체 SO는 삭제됨.
  잔여: 전 타워/적 종류 값 채움 + Soldier(`SoldierData`, 아직 Combat 자체 SO) 이관 (WL-001).
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
