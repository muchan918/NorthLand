# 전투 공간 타워 배치 — 기능 명세

> **상태**: 배치 코어 · **SO 게이트웨이 · 자원 차감 · 타일 버프 · 등장 연출 구현 + 플레이 검증 완료**(2026-08-01) · 낮 전용 게이팅만 미구현(§8)
> **소유**: n0wst4ndup(배치 흐름·게이트웨이·프리뷰·연출) · SUNGSOO(타워 프리팹) · muchan(타워 데이터·자원·페이즈 게이팅) · KSJ(타일 버프)
> **구현 파일**:
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/BattleTile.cs` (타일 마커)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerPlacer.cs` (게이트웨이·배치 코어)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerFootprint.cs` (점유 수명주기·Release/Reoccupy)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerGroupSelectable.cs` (합성 그룹 선택 마커)
> - `Assets/Scripts/GameManager/MouseManager/PlacementRequest.cs` (계약: Snap/CanPlaceAt/OnConfirmed/OnEnded, 히트 인지형)
> - `Assets/Scripts/GameManager/MouseManager/MouseManager.cs` (Snap 위임·히트 전달·OnEnded 발화)
> - `Assets/Scripts/CombatSystem/RangeCircle.cs` (사거리 프리뷰 원 — 공용)
> - `Assets/Scripts/CombatSystem/Vfx/TowerSpawnEffect.cs` (등장 연출 — **임시**, §9.3)
> **관련**: GDD §5.1·§5.8·§6.2, MouseManager #9, 통합 #71, 합성 #263, 연출 #264·#265
> **WatchList**: WL-001 / WL-005 / WL-011 / WL-034 / WL-067 / WL-077 (해소분은 `WatchList-Archive.md`: WL-004 · WL-007 · WL-129)
> **참조**: `Docs/Core/MouseManager.md`, `Docs/Core/TowerMerge.md`, `Docs/Core/InteractionOutline.md`, `Docs/BattleMapBuilder/BattleMapBuilder.md`, `Docs/Review/SystemMap.md`
> 코드가 이 명세와 어긋나면 문서를 갱신한다(팀 계약 #7).

---

## 0. 설계 요지

- **배치 측(n0wst4ndup)이 구현하고 BattleMapBuilder는 변경하지 않는다.** 맵은 타일을 셀 중심에 GameObject로 스폰하므로, **배치 레이캐스트가 맞힌 타일 GameObject가 곧 셀**이다(셀 중심 = 그 타일의 `transform.position`). → 월드↔셀 좌표 변환 **없음**.
- **타일 종류·점유는 타일에 붙인 `BattleTile` 마커**로 안다(맵빌더 질의 API 불요).
- **타워 데이터는 게이트웨이로 주입한다.** `TowerAsset`(SO)이 프리팹·풋프린트·사거리·비용을 전부 제공하고, 배치 코어(`StartPlacement` 이하)는 SO 종류를 모른다.
- **배치는 코어 1개, 진입은 여러 개.** 일반 배치와 합성 결과 배치(#263)가 같은 코어를 타고, 차이는 **비용과 확정 콜백**뿐이다.
- **연출은 로직과 분리된 시각 대역이다.** 배치 확정은 연출을 기다리지 않는다(§9.3).

---

## 1. 목적

낮(경영) 페이즈에 플레이어가 **전투 공간 그리드의 허용된 셀**에 타워를 배치하는 상호작용. 밤에 타워는 사거리 내 적을 자동 공격하지만(GDD §5.2), 본 명세는 **배치까지**만 다룬다. 구현상 MouseManager의 두 미구현 지점(`Snap` 항등, `CanPlaceAt` 항상 true)을 타일 마커 기반 스냅·검증으로 실체화한 것이다.

---

## 2. 범위

**In (구현됨)**
- "허용 셀"(건설 가능) 판정 규칙 (§4)
- 고스트 스냅(풋프린트 중심) → 유효/무효 → 확정 배치 (`PlacementRequest` 히트 인지형, 새 상태 없음)
- 타일 종류·점유 식별 (`BattleTile` 마커), 점유 수명주기 (`TowerFootprint`)
- **W×H 풋프린트** 점유 (타워별 GridWidth×GridHeight, 타일별 bool 다중 셀)
- **사거리 미리보기** (`RangeCircle` — 채움 + 외곽선, 타일 버프 반영)
- **풋프린트 셀 하이라이트** (셀별 유효/무효 색)
- **`TowerAsset` 게이트웨이** (§6.2) — 더미 경로를 대체
- **자원 차감** (`ManagementController` 경유, §8)
- **타일 버프 적용** (점유 타일의 버프를 중첩 규칙으로 합산 → `TowerTileBuff`, §7)
- **등장 연출** (§9.3) — **룩·수치 전부 임시**

**Out (훅/예정)**
- **낮 전용 게이팅** → §8 (유일하게 남은 훅)
- 타워 스탯·공격·투사체 → Combat
- 타워 정보 패널 연동 → WL-011
- 철거/판매 → §12

---

## 3. 용어 · 좌표

| 용어 | 정의 |
| --- | --- |
| 셀(Cell) | 배치 최소 단위 = 전투 맵 타일 1개(GameObject). `BattleTile` 마커를 가진다 |
| 셀 중심 | 타일 GameObject의 `transform.position` (맵이 타일을 셀 중심에 스폰) |
| 풋프린트 | 타워가 점유하는 W×H 셀 블록 (앵커 = 히트한 타일, +X/+Z로 확장) |
| TileKind | Grass(건설가능) / Road(경로) / Lava(위험) |
| 점유 | `BattleTile.Occupied` (타일별 런타임 bool) |

**좌표**: 배치 측은 월드↔셀 변환을 하지 않는다. 히트한 **타일 GameObject를 직접** 다루고, 풋프린트의 이웃 셀은 `tileSize` 간격으로 앵커 주변 지점을 공간 질의(`OverlapSphere(cell, tileSize*0.4f)`)해 찾는다. **그리드가 월드 X/Z축에 정렬돼 있다고 가정**한다(battlespace 회전 없음).

**`tileSize` 출처 (WL-034)**: `TowerPlacer.Awake`가 씬의 `CombatMapGenerator.Settings.TileSize`를 찾아 인스펙터 값을 **덮어쓴다**. 인스펙터 `tileSize`(기본 5)는 구맵·테스트 씬 폴백이다.

| 출처 | 값 | 비고 |
| --- | --- | --- |
| `CombatMapGenerationSettings.asset` (신맵 정본) | **15** | `Assets/Personal/SUNJIN/ScriptableObjects/Setting/` |
| `CombatMapGenerationSettings.TileSize` (클래스 기본) | 5 | 에셋이 덮어씀 |
| `TowerPlacer.tileSize` (인스펙터) | 5 | 신맵이 있으면 Awake에서 15로 대체 |
| `StageBuilder.TileSize` (구맵) | 5 | 신맵 반영 후에도 다르면 Awake가 **경고**만 |

> 신맵 타일이 15인데 5가 남으면 하이라이트 쿼드가 타일의 ⅓ 크기로 그려지고 다중 셀 풋프린트가 어긋난다. 완전한 단일 출처화는 WL-034(공용 셀↔월드 변환 유틸)에서.
>
> ⚠ `TowerPlacer.cs:35` 주석의 `WL-032`는 **WL-034 오기**다(WatchList에 기록됨). WL-032는 GameManager API 미등재 건으로 무관하다.

---

## 4. 허용 위치 규칙

배치 가능 = 앵커(히트 타일) 기준 **W×H 풋프린트의 모든 셀**이 `BattleTile`을 갖고 **`Kind == Grass` && `Occupied == false`**, 그리고 **자원이 충분할 것**. 확정 시 풋프린트 전 셀을 점유한다.

셀 하나의 판정이 함의하는 것:
- **인맵**: 타일 존재 ⇒ 생성된 블록 안. 맵 밖이면 `BattleTile` 없음 → 무효.
- **비도로·비용암**: 스폰된 타일은 grass/road/lava 중 정확히 하나. Grass ⇒ 도로도 용암도 아님.
- **비특수**: 스폰 지점·최종 목표는 경로(도로) 위 또는 별도 오브젝트 → Grass 아님 → 자동 제외.
- **미점유**: `!Occupied`.

**자원**: `CanPlaceFootprint`가 `ManagementController.CanAfford`를 함께 본다 → 자원이 모자라면 고스트가 **빨강**으로 유지돼, 연속 배치(`keepPlacing`) 중 소진돼도 즉시 읽힌다.

> **결정(Q1)**: 별도 '건설가능' 타일 지정 없이 **grass 타일 = 건설 가능**. W/H는 `TowerAsset.Data.GridWidth/GridHeight`(muchan CSV, 현재 5종 모두 1×1).

---

## 5. 시스템 책임 분담

| 단계 | 소유 | 비고 |
| --- | --- | --- |
| 포인터 입력·레이캐스트·고스트·상태·**히트 전달** | **MouseManager** | 그리드 규칙 무지(제네릭). 계약 #1·#6 |
| 게이트웨이 진입·스냅·풋프린트 검증·생성·점유·프리뷰 | **TowerPlacer** (n0wst4ndup) | 배치 규칙 전부 여기 |
| 타일 종류·점유 데이터 | **BattleTile** 마커 | 타일에 부착 — §6.1 |
| 점유 수명주기(파괴 시 해제 / 합성 시 임시 해제·복원) | **TowerFootprint** | `Occupy`/`Release`/`Reoccupy` — `TowerMerge.md` |
| 타워 프리팹·스탯·조립 | **Combat** (`Tower.Build`) | 배치가 넘긴 SO로 조립(WL-129 해소) |
| 자원 차감 | **Management** (`ManagementController.CanAfford`/`TrySpend`) | WL-017 — 소비처는 지갑에 직접 접근하지 않는다 |
| 타일 버프 합산·적용 | **TileBuffCalculator** / **TowerTileBuff** (KSJ) | 중첩 규칙 = `TileBuffRuleSettings` |
| **등장 연출** | **TowerSpawnEffect** (n0wst4ndup) | 시각 전용·논블로킹 — **임시**(§9.3) |
| 정보 패널 | **UI** (`TowerInfoUI`) | WL-011 |
| **BattleMapBuilder** | **코드 변경 없음** | 타일이 `BattleTile`을 갖게 하는 건 와이어링(§6.1) |

---

## 6. 메커니즘 (타일 마커 + 데이터 게이트웨이)

### 6.1 타일 식별 (`BattleTile` 마커)
- `BattleTile { TileKind Kind; bool Occupied }` — 데이터 전용 컴포넌트.
- 배치 히트에서 `hit.collider.GetComponentInParent<BattleTile>()`로 타일을 얻는다. 셀 중심 = `tile.transform.position`, 배치 y = `tile.AnchorPosition.y`(타일 윗면 — 레이가 옆면에 맞아도 타워가 윗면에 앉는다).
- 풋프린트 이웃 셀은 앵커 위치에서 `tileSize` 간격으로 계산한 지점을 `OverlapSphere`로 조회해 각 `BattleTile`을 찾는다.

**타일 태깅 (완료)**: 전투 타일 프리팹에 `BattleTile`(Kind 설정)이 부착돼 있다(`Assets/Imported/@NorthLand/Prefabs/Tile/`의 GrassTile 4종·ground_cube). 인스턴스는 `Instantiate` 시 이를 그대로 가지므로 스포너의 별도 태깅이 불필요하다. 단 프리팹이 **`Assets/Imported/`(벤더링, 별도 git 저장소)** 에 있어 **메인 repo diff·자동 리뷰에는 이 부착이 보이지 않는다.**

> **WL-067 (열림)**: 전투맵 타일 계약이 이원화돼 있다 — 스킬 조준(MouseManager)은 신맵 `CombatMapTileView.TileType`을, 배치 검증은 `BattleTile.Kind`를 본다. 배치는 정상 동작하지만 두 계약 병존 자체가 문제다. 해소 방향은 `TowerPlacer`를 `CombatMapTileView` 질의로 이관.

### 6.2 데이터 게이트웨이 (`TowerAsset` 주입 — 구현됨)

TowerPlacer가 배치에 쓰는 값은 `TowerPlacementData { GridWidth, GridHeight, AttackRange }` + 프리팹(tower/ghost)이다. **진입 방식과 무관한 코어 `StartPlacement(TowerPlacementData, ...)`** 를 두어 SO 종류에 결합하지 않는다.

```csharp
// 일반 배치 — 비용은 so.Cost, 확정 콜백 없음
bool BeginTowerPlacement(TowerAsset so);

// 합성(#263) — 결과 코스트로 배치하고, 확정 직후 재료 소모를 실행
bool BeginTowerPlacement(TowerAsset so, IReadOnlyList<ResourceCost> cost,
                         Action onConfirmed, Action onEnded = null);
```

- **반환값 = 배치 세션이 실제로 시작됐는가.** `false`면 `onEnded`도 **영영 오지 않는다** → 호출부가 배치 동안 유지하려던 상태를 걸어두면 안 된다는 신호다(합성이 이 신호로 커맨드를 즉시 `Undo`한다).
- `onEnded`는 확정/취소 **무관하게** 세션 종료 시 1회. **어느 쪽으로 끝났는지는 알려주지 않는다** — 구분이 필요한 소비처는 자기 상태로 판단해야 한다(`TowerMergeCommand`가 그렇게 한다).
- **프리뷰 사거리 해석**은 `TowerBehaviourFactory.ResolveAttackFields(so)` 단일 출처를 쓴다(WL-079). 공격 스탯이 없고 `TowerType == Magic`이면 `TowerAsset.MagicRadius`(오라 반경), 둘 다 없으면 LogError + `false`.

> ⚠ **콜백 등록 순서**: `_onConfirmed`/`_onEnded`는 반드시 `MouseManager.BeginPlacement` **이후**에 대입한다. `BeginPlacement` 내부의 `CancelPlacement`가 **이전** 배치의 `OnEnded`를 발화해 두 필드를 소비·null 처리하기 때문이다. 먼저 대입하면 합성 재료 소모 콜백이 유실돼 **무료 합성**이 된다. 프리뷰 생성도 같은 이유로 `BeginPlacement` 이후다.

인스펙터의 더미 필드(`dummyGridWidth`/`dummyGridHeight`/`dummyAttackRange`)는 SO 경로가 들어오며 **사용되지 않는 잔재**다 — 제거 대상(§11).

---

## 7. 배치 흐름 (구현)

**`PlacementRequest`(히트 인지형)** — MouseManager는 그리드/스냅 규칙을 모르고 요청이 소유:
- `Func<RaycastHit, Vector3> Snap` (null이면 `hit.point`)
- `Func<RaycastHit, bool> CanPlaceAt`
- `Action<RaycastHit, Vector3> OnConfirmed`
- `Action OnEnded` — 취소/확정 복귀 시 프리뷰 정리(선택, null 허용)

**흐름** (Idle/Placement 2상태, 새 상태 없음):
- 진입: 패널/버튼 → `BeginTowerPlacement(so)` → `StartPlacement` → `MouseManager.BeginPlacement`.
- 매 프레임(Placement): 레이캐스트 → `Snap(hit)`(풋프린트 중심) + 프리뷰 갱신 → 고스트 이동 → `CanPlaceAt(hit)`.
- 확정(좌클릭·유효): `OnConfirmed(hit, pos)` → 아래 확정 순서 → (`keepPlacing=false`면) Idle 복귀.
- 취소(우클릭/Esc) / 확정 복귀: `OnEnded` → 프리뷰 정리 → 종료 통지.

**확정 순서 (`PlaceTower`)** — 순서 자체가 계약이다:

1. 앵커 재취득 + `RebuildFootprint` + 전 셀 재검증 (프레임당 1회라 캐시를 믿지 않고 재확인)
2. `ManagementController.TrySpend(_activeCost)` — 실패 시 배치 취소 (씬에 없으면 무료 = 테스트 씬)
3. `Instantiate(towerPrefab, snappedPos, identity)`
4. `TowerFootprint` 부착 + 전 셀 `Occupy` — 인스턴스가 점유를 기억해야 파괴 시 되돌릴 수 있다
5. `TowerGroupSelectable` 부착 — 합성 재료 후보로 등록(결과 타워도 이 경로라 다단 합성 가능)
6. **`TowerTileBuff.Initialize(...)`** ← **반드시 `Tower.Build` 앞**
7. `Tower.Build(_activeAsset)` — **패널에서 산 SO가 프리팹이 문 SO를 이긴다**(WL-129). 다르면 경고 후 산 쪽으로 재조립. `Tower` 없는 프리팹은 LogError
8. `onConfirmed` 1회 실행 (먼저 비우고 호출 → `keepPlacing`에서 재실행 방지)
9. **`TowerSpawnEffect.Play(...)`** — 로직이 전부 끝난 뒤 마지막 (§9.3)

> **6 → 7 순서를 뒤집으면 안 되는 이유**: 버프 오라는 **조립 시점에 자기 반경으로 대상을 한 번 훑는데**, 그 반경이 타일 버프(사거리)에 의존한다. 순서가 뒤바뀌면 첫 적용이 버프 이전 반경으로 계산된다. 구 `AuraTower`가 `Start`에서 반경을 재계산하던 우회로가 정확히 이 문제였고, 여기서 순서를 정해 그 우회로를 없앴다.

**TowerPlacer 판정**:
- `Snap`: 앵커 기준 풋프린트 **중심**으로 스냅(y = 타일 앵커). 앵커가 바뀐 프레임에만 풋프린트를 재구성하고 사거리 원을 갱신한다.
- `CanPlaceAt`: 풋프린트 전 셀이 `Grass && !Occupied` + `CanAfford`. **`Snap`이 채운 캐시를 신뢰한다**(MouseManager가 매 프레임 Snap → CanPlaceAt 순으로 호출).
- `OnEnded`: 프리뷰 정리 → `_onConfirmed` 폐기(취소로 끝났으면 재료 보존) → 종료 통지를 **먼저 비우고** 호출(구독자가 그 안에서 새 배치를 시작해도 중복 발화 없음).

**전제(와이어링)**: 타일이 `_placementMask`(Ground) 레이어 + Collider 보유, 씬에 `MouseManager` 존재, `TowerAsset`에 tower/ghost 프리팹 지정(고스트는 Collider 없음), `TowerPlacer.tileBuffRules`에 `TileBuffRuleSettings` 지정.

---

## 8. 통합 훅 (자원 · 페이즈)

| 훅 | 상태 | 구현/잔여 |
| --- | --- | --- |
| **자원 차감** | ✅ 구현 | `ManagementController.CanAfford`(검증) / `TrySpend`(확정). 비용 출처 = `TowerAsset.Cost`. 컨트롤러가 씬에 없으면 **무료 배치**(경영 없는 테스트 씬 지원) |
| **낮 전용 게이팅** | ❌ 미구현 | `TowerPlacer` 진입에 `DayNightManager.CurrentPhase` 확인이 없다. 완화만 존재 — `PhasePanelSwitcher.ShowNight`가 밤 진입 시 진행 중 배치를 취소한다. 합성 실행부 축은 **WL-077**(muchan) |

> 자원은 `ResourceWallet`에 직접 접근하지 않고 **반드시 `ManagementController` 경유**다(WL-017). `TrySpend`는 원자적 — 전부 감당 가능할 때만 전부 차감한다.

---

## 9. 시각 피드백

### 9.1 사거리 미리보기 (구현)

배치 중 고스트 위치에 `RangeCircle`(공용 컴포넌트)로 **채움 + 굵은 외곽선** 원을 표시한다. `rangeFillColor`(반투명) / `rangeColor`(외곽선).

- 첫 스냅 전에는 `Hide()` — 그러지 않으면 맵 원점(0,0)에 원이 노출된다.
- 반경은 **타일 버프를 반영한 프리뷰 값**이다: `(기본사거리 + Flat) × (1 + Percentage/100)`. 앵커가 바뀐 프레임에만 재계산한다.
- 같은 반지름이면 `RangeCircle`이 지오메트리 재생성을 생략한다.

**부모 스케일 역보정은 매 표시마다 재계산한다**(`Show`/`SetRadius`/`LateUpdate`). 생성 1회 캡처는 그 순간의 부모 스케일에 영원히 고정되는데, 등장 연출(§9.3)이 타워 루트를 0→원본으로 애니메이션하면서 **과도기에 원이 생성되는 경로가 실제로 생겼다** — 배치 직후 팝 구간에 그 타워를 드래그 선택하면 `Tower.ShowRangeCircle`이 거기서 원을 만들고 캐시하므로, 보정이 예컨대 1/0.41≈2.44로 굳어 그 타워의 사거리 원이 **이후 계속 2.44배**로 표시된다(#192가 막으려던 증상의 재발 경로). 부모 스케일이 1로 고정된 대상에서는 동작이 이전과 동일하다.

### 9.2 풋프린트 셀 하이라이트 (구현)

풋프린트 각 셀에 바닥에 눕힌 반투명 쿼드를 **유효=`validCellColor`(초록) / 무효=`invalidCellColor`(빨강)** 로 표시한다. 표시 y는 타일 윗면 + 0.03(z-파이팅 방지). 고스트 자체의 유효/무효 색은 이 하이라이트로 대체돼 별도 불필요.

### 9.3 등장 연출 (`TowerSpawnEffect`) — #264

> # ⚠ 이 절 전체는 임시다
>
> **룩·수치·연출 형태 어느 것도 확정이 아니다.** 지금 상태는 "타워 에셋이 임시인 동안 먼저 만들어 둔 것"이고, 아트 방향이 정해지면 **통째로 바뀔 수 있다**. 나중에 이 문서를 보는 사람이 아래를 확정 설계로 오해하면 안 된다.
>
> **검증 완료(§9.3.4)와 설계 확정은 다르다.** 아래 수치는 "플레이에서 이상하지 않다"까지만 확인된 것이지 아트가 고른 값이 아니다. 임시인 이유는 검증이 부족해서가 아니라 **타워 에셋이 임시이고 아트 방향이 미정**이기 때문이며, 그 두 조건은 검증과 무관하게 그대로다.
>
> 다만 **§9.3.2 계약은 임시가 아니다** — 그게 이 연출이 에셋 교체를 견디는 유일한 장치이고, 실제로 에셋 교체 회귀 검증을 통과한 근거다.

#### 9.3.1 무엇을 하는가 (임시)

배치가 확정되면:

```
확정 → 타워 주변 공중의 하얀 알갱이가 소용돌이로 빨려들며 수렴
     → 타워가 오버슈트로 튀어나옴(스케일 0 → 원본, back-out)
     → 동시에 바닥에 충격파 링이 퍼지며 사라짐
```

**시각 전용·논블로킹**이다. 호출 시점에 타워는 이미 논리적으로 완성돼 있고(§7의 1~8단계 완료) 연출은 그 위에 얹히기만 한다. 연출 도중 밤 전환·새 배치가 들어와도 상태가 어긋나지 않는다.

#### 9.3.2 계약 — **이 부분은 유지해야 한다**

`TowerSpawnEffect`는 **타워를 모른다.**

```csharp
void      Play(Transform target, float footprintSize);                              // fire-and-forget
UniTask   PlayAsync(Transform target, float footprintSize, CancellationToken ct);   // #265 합성용
Bounds    CalculateVisualBounds(Transform target, float footprintSize);             // 공용
```

진입점이 받는 것은 `Transform`과 풋프린트 크기뿐이다. `Tower`도 `TowerAsset`도 메시도 받지 않고, 대상에서 읽는 것은 **`Renderer.bounds`와 `localScale`이 전부**다.

> ### ⚠ 이 연출은 대상 루트의 `localScale`을 **배타적으로 소유**한다
>
> 읽기만 하는 게 아니라 **쓴다** — 시작 시 0으로 덮고, 등장 구간(back-out)을 거쳐 캡처한 원본으로 되돌린다. 대상이 안 보이는 창은 수렴 구간 약 **0.45초**이고, 스케일이 과도기 값인 창은 등장 구간 약 **0.28초**다.
>
> 이 한 문장이 아래 세 가지의 공통 뿌리다. 새 소비처를 붙이기 전에 반드시 확인할 것:
>
> | 파생 문제 | 현재 방어 |
> | --- | --- |
> | 같은 대상에 두 번 재생 → 두 번째가 **0을 원본으로 캡처** → 타워 영구 투명 | 진입점의 대상별 in-flight 레지스트리. 재생 중이면 **기존 연출을 먼저 원복시키고 인계**한다 |
> | 과도기 스케일을 다른 시스템이 **캡처해 굳힘** | `RangeCircle`이 부모 스케일 역보정을 생성 1회가 아니라 표시할 때마다 재계산(§9.1) |
> | 대상 루트 스케일을 쓰는 **`Animator`** 가 새 에셋에 붙으면 서로 덮어씀 | **방어하지 않는다** — 눈에 즉시 보이는 실패라 발생 시 시각 자식만 스케일하는 형태로 바꾼다 |
>
> 콜라이더도 이 창 동안 함께 죽는다. 단 **드래그 선택은 콜라이더가 아니라 위치 기반**(`MouseManager.RefreshBoxHits`)이라 스케일 0인 타워에도 도달한다 — "연출 중엔 선택이 안 된다"고 가정하면 안 된다.
>
> 공격은 문제가 되지 않는다. `AttackBehaviour.ActivePhase == NightOnly`이고 `Tower.Update`가 낮이면 Tick을 건너뛴다.

**왜 이렇게까지 하는가**: 타워 에셋이 임시라 통째로 교체될 예정이기 때문이다. 연출이 특정 메시·머티리얼·계층 구성을 조금이라도 참조하면 교체와 함께 깨진다. 그 결과 **대상이 타워일 필요조차 없어서** Renderer가 달린 큐브에 그대로 재생되고, 에셋 없이 연출을 튜닝·검증할 수 있다.

**수치 앵커를 둘로 나눈다. 이게 설계의 전부다:**

| 앵커 | 무엇을 정하나 | 왜 |
| --- | --- | --- |
| **풋프린트**(논리 크기) | 알갱이 크기 · 바닥 링 반경 | 타일은 항상 15인데 타워 메시는 제각각이다(**현재 프리팹만 봐도 높이 2.0~37.7, 19배**). bounds에 묶으면 스케일이 어긋난 프리팹에서 알갱이까지 어긋난다. 풋프린트는 그리드가 정하는 값이라 **에셋 교체와 무관하게 불변**이고, 덕분에 모든 타워의 알갱이가 같은 크기로 보여 하나의 시각 언어가 된다 |
| **bounds**(시각 크기) | 입자 개수 · 구름 모양 | 큰 타워는 알갱이가 많아야 하고(30~90), 후광은 **그 타워의** 실루엣을 감싸야 한다 |

> **한 문장 규칙**: **크기·바닥은 논리(풋프린트), 분포·개수는 시각(bounds).** 새 요소를 추가하거나 #265가 재료 타워에 같은 규칙을 적용할 때 위 표를 재해석하지 말고 이 문장을 따를 것.

지켜야 할 규칙:

- **길이 상수를 코드에 두지 않는다.** 전부 위 둘 중 하나에 곱하는 비율이다. 시간만 절대값 — 길이가 아니라 에셋과 무관하다.
- **시간은 `Time.unscaledDeltaTime`을 쓴다.** 배속·일시정지는 전역 `Time.timeScale`이라(`GameSpeedController.ApplyTimeScale`) 스케일드 시간을 쓰면 **일시정지 중 타워가 스케일 0인 채로 멈춘다** — "안 보이는 타워"가 정지 버튼 하나로 재현된다. x4 배속에서 수렴이 0.11초로 줄어 연출이 소실되는 것도 같은 원인이다. 순수 시각·논블로킹이라 게임플레이 타이밍과 분리해도 된다(WL-100/WL-119와 같은 축).
- **`transform.position`이 아니라 `bounds.center` / `bounds.min.y`** — 새 에셋의 피벗이 밑면이 아닐 수 있다.
- **`Vector3.one`이 아니라 캡처한 원본 스케일**로 되돌린다 — 새 에셋 루트 스케일이 1이라는 보장이 없다.
- **메시 정점을 한 번도 읽지 않는다.** 프로젝트 FBX 1664개 중 573개가 `isReadable: 0`이고 신규 에셋이 어느 쪽일지 보장이 없다(#264 조사). `Renderer.bounds`는 정점을 읽지 않는다. → 입자를 실루엣 모양으로 흩뿌릴 수 없는 것도 같은 제약이며, 중심 수렴 형태는 그 제약이 고른 형태다.
- **정리는 `OnDestroy` 단일 지점.** 씬 전환·취소·예외 어느 경로로 끊겨도 타워가 스케일 0으로 남지 않는다 — **안 보이는 타워가 이 연출의 최악의 실패 모드**다.

#### 9.3.3 현재 수치 (전부 임시 — 자유롭게 바꿀 것)

플레이에서 **이 값 그대로 통과**했다(§9.3.4). 조정 없이 넘어갔다는 뜻이지 아트가 고른 값이라는 뜻은 아니다.

| 항목 | 현재 값 | 근거 |
| --- | --- | --- |
| 수렴 / 등장 / 링 지속 | 0.45s / 0.28s / 0.38s | 감으로 정한 값이 플레이에서 그대로 통과 |
| 알갱이 크기 | 풋프린트 × 0.15 (=2.25) | 게임 줌 70·1080p에서 17.4px |
| 알갱이 화면 하한 / 상한 | `orthoSize × 0.017` / 풋프린트 × 0.4 | 줌 300에서도 보이게 / 칸을 뒤덮지 않게. 줌 전 구간 확인됨 |
| 입자 개수 | `∛(bounds 부피) × 4`, clamp 30~90 | — |
| 후광 두께 | 풋프린트 × 0.55 | 표면 + 이 두께에 난수(0.6~1.4배) |
| 바닥 링 반경 | 풋프린트 × 0.62 (=9.3) | — |
| 색 | 흰색 고정 | **아트 TBD** — `OutlineHighlight`의 임시 색과 같은 성격 |

구현 선택(임시):
- 입자 = **GameObject + 빌보드 쿼드**. 수렴이 좌표 보간이라 attractor가 없는 `ParticleSystem`으로는 매 프레임 `SetParticles` 개입이 필요하고, 이 규모에서 instancing 이득은 없다. 프로젝트에 파티클 저작 파이프라인이 아직 **없다**는 것도 이유다.
- 셰이더 = `Sprites/Default`(프로젝트 표준 반투명 언릿 — URP PC/Mobile 양쪽 동작, 신규 셰이더 에셋 불필요). 알갱이 텍스처는 절차 생성(`VortexVisual` 선례).
- 바닥 링 = `RangeCircle` 재사용(채움 투명 + 외곽선만).
- 빌보드 회전은 **1회 고정** — 직교 카메라라 매 프레임 카메라를 향할 필요가 없다.

#### 9.3.4 검증 상태 — 전 축 통과 (2026-08-01)

| 축 | 상태 | 방법 |
| --- | --- | --- |
| 컴파일 | ✅ | `editor refresh --compile` + `console --type error` |
| 파생 수치 (9개 대상, 40배 스케일 범위 + 피벗 오프셋 + Renderer 없음 폴백) | ✅ | 편집 모드 exec |
| 정지 프레임 룩 (게임 줌 70 · 1920×1080) | ✅ | 편집 모드 결정론적 캡처 |
| **움직임** (수렴 이징 · 소용돌이 · 오버슈트 · 링) | ✅ | 플레이 모드 |
| **실제 배치 클릭 경로** (패널 → 확정 → 연출) | ✅ | 플레이 모드 — 자원 차감·점유·`Tower.Build`와 충돌 없음 |
| **줌 70~300 전 구간** | ✅ | 플레이 모드 — 화면 하한/상한이 실제로 맞음 |
| **에셋 교체 회귀** (크기가 크게 다른 대상) | ✅ | **코드 수정 없이 동작 — §9.3.2 계약이 실제로 성립함을 확인** |

**수치 조정은 없었다** — §9.3.3 값 그대로 통과했다.

> 이 표가 채워졌다고 연출이 확정된 것은 아니다(절 상단 경고). 검증된 것은 "지금 값이 동작하고 이상하지 않다"이지 "이 룩으로 간다"가 아니다.

#### 9.3.5 #265(합성 소모 연출)와의 관계

`PlayAsync`가 **합성 결과 배치의 마지막 구간**이다. 두 연출이 구분되면 안 되므로 #265는 이 함수를 **그대로** 불러야 하고, 앞 구간(재료 화이트아웃 → 폭발 → 부유 → 수렴)만 따로 만든다. `CalculateVisualBounds`를 public으로 둔 것도 #265가 재료 타워의 크기를 **같은 규칙으로** 재게 하기 위함이다.

---

## 10. 인수 조건 (Acceptance Criteria)

- [x] 고스트가 풋프린트 중심으로 스냅된다.
- [x] road·lava·타일없음·점유 셀이 풋프린트에 포함되면 무효(빨강 하이라이트), 좌클릭 무반응.
- [x] 풋프린트 전 셀이 grass·미점유면 유효(초록), 좌클릭 시 타워가 중심에 생성되고 전 셀 점유.
- [x] 점유된 셀에 겹쳐 배치 불가.
- [x] 우클릭/Esc로 취소, 프리뷰 정리.
- [x] 사거리 미리보기 원이 **타일 버프를 반영한** 사거리로 표시된다.
- [x] `TowerAsset` 주입으로 배치되고, 배치된 인스턴스가 **그 SO로** 조립된다(WL-129).
- [x] 자원 부족 시 고스트가 빨강, 확정해도 배치되지 않는다.
- [x] 타워가 파괴되면 점유 타일이 해제돼 재배치가 가능하다(`TowerFootprint`).
- [x] 배치 확정 시 등장 연출이 재생되고, 줌 70~300 전 구간에서 알갱이가 보인다(§9.3.4).
- [x] **크기가 크게 다른 대상에서도 코드 수정 없이 연출이 동작한다** — 연출이 에셋에 결합돼 있지 않다.
- [ ] **밤 진입 차단** — §8, 미구현.

검증: 개인 테스트 씬 Play 확인(팀 컨벤션 — 유닛 테스트 없음). 시각 검증은 `Docs/Tools/unity-cli-guide.md` §4.J의 결정론적 캡처 루프.

---

## 11. TODO / 의존

**배치 본체**
- **낮 전용 게이팅**(§8) — 유일하게 남은 통합 훅. 합성 실행부 축은 WL-077(muchan).
- **더미 필드 제거** — `dummyGridWidth`/`dummyGridHeight`/`dummyAttackRange`는 SO 경로가 들어오며 사용되지 않는 잔재다.
- **WL-034 (`tileSize` 이중화, PARTIAL)** — 신맵은 단일 출처화됐으나 `TowerPlacer.tileSize`와 `StageBuilder.TileSize`가 여전히 독립이고, 좌표 변환식 사본이 여러 곳에 있다. 공용 셀↔월드 변환 유틸로 흡수 예정. (코드 주석의 `WL-032` 표기는 오기 — §3)
- **WL-067 (타일 계약 이원화)** — 배치는 `BattleTile.Kind`, 스킬 조준은 `CombatMapTileView.TileType`. `TowerPlacer`를 후자로 이관.
- **WL-011 (선택 통지 이중 경로)** — 타워 정보 패널 연동에 영향.
- **WL-001 (PARTIAL)** — `lightning_tower`(TowerType=Chain)만 Attack/ChainRadius/MaxChainTargets가 전부 0이라 **배치해도 무동작**이다.
- **WL-005 (PARTIAL)** — 대상 탐지를 LayerMask로 할지 Tag로 할지 미확정.

**연출(§9.3) — 검증은 끝났고(§9.3.4), 남은 것은 "할 일"이라기보다 "열려 있는 결정"이다**
- **아트 방향 확정 시 룩 전면 재검토.** 색·형태·지속 시간 어느 것도 고정이 아니다. 유지해야 하는 것은 §9.3.2 계약뿐이고, 나머지는 갈아엎어도 된다.
- **타워 에셋이 실제로 교체될 때 재확인** — 크기가 크게 다른 대상으로 한 회귀는 통과했지만(§9.3.4), 실 교체 시점에 한 번 더 재생해 보는 것이 싸다. **코드 수정이 필요해지면 계약이 깨진 것이다.**
- **#265 합성 소모 연출** — 앞 구간만 추가하고 이 함수를 재사용.

**해소됨** (`WatchList-Archive.md`)
- ~~WL-004 (배치 검증 공백)~~ — `BattleTile` + `TowerPlacer`로 해소.
- ~~WL-007 (좌표 이원화)~~ — 배치 측은 변환하지 않음. 단 그리드 축 정렬 가정 + WL-034에 의존.
- ~~WL-129 (산 SO ≠ 배치된 SO)~~ — `Tower.Build(_activeAsset)`로 해소(§7-7).

**기타**: 점유 수명주기는 타일별 플래그 + `TowerFootprint.OnDestroy`라 맵 리셋(타일 파괴/재생성) 시 자동 초기화된다.

---

## 12. 확장 여지

- **가변 풋프린트**: 코드는 W×H 지원(구현됨). 현재 CSV 데이터가 전부 1×1이라 시각적으론 1×1 — designer가 CSV GridWidth/Height를 키우면 즉시 확대. (타워 프리팹 자체의 W×H 비주얼은 별도.)
- **철거/판매·재배치**: `TowerFootprint.Release` + 자원 환급. 점유 해제 API는 합성(#263)이 이미 만들어 뒀다.
- **허용 규칙 강화**: '도로 인접만' 또는 맵빌더 '건설가능' 타일 명시(Q1 대안).
- **지원 타워**(haste_tower 등, WL-026): 인접 셀 버프 — 배치 규칙엔 영향 없음.
- **연출 재사용**: `TowerSpawnEffect`는 타워를 모르므로 건물 건설·영토 확장 등 "무언가 등장하는" 다른 자리에도 그대로 쓸 수 있다. 다만 지금 룩이 임시라 재사용을 전제로 설계를 굳히지는 말 것.
