# 전투 공간 타워 배치 — 기능 명세

> **상태**: 배치 코어 · **SO 게이트웨이 · 자원 차감 · 타일 버프 · 등장 연출 구현 + 플레이 검증 완료**(2026-08-01) · **되돌리기 구현**(#281, 2026-08-03 · #444에서 경영 조작까지 확장, 2026-08-22) · 낮 전용 게이팅만 미구현(§8)
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
> **관련**: GDD §5.1·§5.8·§6.2, MouseManager #9, 통합 #71, 합성 #263, 연출 #264·#265, 타워 구조 #274
> **WatchList**: WL-001 / WL-005 / WL-011 / WL-034 / WL-067 / WL-077 (해소분은 `WatchList-Archive.md`: WL-004 · WL-007 · WL-129)
> **참조**: `Docs/Core/Tower.md`(배치되는 타워 본체 — 조립·스탯·데이터), `Docs/Core/MouseManager.md`, `Docs/Core/TowerMerge.md`, `Docs/Core/InteractionOutline.md`, `Docs/BattleMapBuilder/BattleMapBuilder.md`, `Docs/Review/SystemMap.md`
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

**좌표**: 배치 측은 월드↔셀 변환을 하지 않는다. 히트한 **타일 GameObject를 직접** 다루고, 풋프린트의 이웃 셀은 `tileSize` 간격으로 앵커 주변 지점을 공간 질의(`OverlapSphere(cell, tileSize*0.4f)`)해 찾는다.

**그리드 축 = 타일 루트(`CombatMapTileSpawner.CoordinateRoot`)의 회전**(2026-08-04 확정).
이전 판은 **월드 X/Z축 정렬을 가정**했는데, 맵 루트를 회전시키자 깨졌다(실제 발생: `MapBuilder` Y **59.45°**).

- `TowerPlacer`가 `Awake`에서 `CombatMapTileSpawner.CoordinateRoot`(공개 프로퍼티)를 `_gridRoot`로 잡고,
  `GridBasis`(= `_gridRoot.rotation`)를 이웃 셀 오프셋(`GridStep`)·셀 하이라이트 회전·배치물/고스트 회전에 쓴다.
- `tileSize`와 **같은 출처**(스포너)에서 축을 받으므로 앵커 유무와 무관하다.
- 스포너가 없는 씬(구맵·테스트)에서는 identity로 떨어져 기존 월드 축 동작이 유지된다.

> **이력 주의** — 이 절은 하루 안에 두 번 바뀌었다. 1차 수정은 **앵커 타일의 회전**을 기준축으로 썼고
> "`tileRoot`를 노출할 필요가 없다"고 적었다. 그것이 성립한 이유는 타일이 `localRotation = identity`로
> 생성된다는 전제뿐이어서, **타일 프리팹에 랜덤 yaw(반복감 제거)가 들어오면 셀마다 축이 튄다.**
> 그래서 2차로 타일 루트를 공개해 출처를 통일했다(현행). 옛 서술(`_gridBasis`를 앵커에서 읽는다)은 폐기다.

| 증상 | 수정 전 | 수정 후 |
|---|---|---|
| 셀 하이라이트 쿼드 방향 | 월드 고정 `Euler(90,0,0)` → 타일 경계와 **59.45° 어긋남** | `GridBasis * Euler(90,0,0)` → 각도차 **0.0000°** |
| 이웃 셀 (1,0) 위치 | 월드 X로 이동 → 정답에서 **5.95유닛** 벗어남(`OverlapSphere` 반경 2.4 초과 → 타일 못 찾음) | 맵 로컬 X로 정확히 `tileSize` 이동 |

⚠️ 쿼드 회전은 `basis * Euler(90,0,0)` **순서**여야 한다 — 뒤바꾸면 축이 틀어진다. 그리고 쿼드 회전을 `CreateCellHighlights`(배치 시작 1회)에서 `UpdateCellHighlights`(매 프레임)로 옮겼다.

> 셀 하이라이트 어긋남만 눈에 보였던 이유는 **현재 타워 9종이 전부 1×1**이라서다(`TowerTable.csv`). W=H=1이면 이웃 셀 오프셋과 중심 오프셋이 모두 0이 되어 위치 버그가 드러나지 않는다. 다중 셀 타워가 추가되는 순간 배치 자체가 불가능해질 잠재 버그였다.

**타워 본체와 고스트도 그리드 축에 맞춘다** (2026-08-04). 이전에는 `Instantiate`에 회전 인자가 없어 identity로 놓여, 회전된 그리드 위에 월드 정렬된 타워가 앉았다.

- 배치물: `Instantiate(towerPrefab, snappedPos, GridBasis)`
- 고스트: `PlacementRequest.GhostRotation`(신설, 기본 identity)에 요청 측이 그리드 기준축을 넣는다.
  배치 세션 동안 상수라 매 프레임 갱신하지 않는다 — 맵 루트는 런타임에 돌지 않는다.
- `PlacementButton`(테스트 헬퍼)은 `Snap`이 없어 그리드에 붙지 않으므로 identity 기본값이 맞다 — 손대지 않았다.

**`tileSize` 출처 (WL-034)**: `TowerPlacer.Awake`가 씬의 `CombatMapGenerator.Settings.TileSize`를 찾아 인스펙터 값을 **덮어쓴다**. 인스펙터 `tileSize`(기본 5)는 구맵·테스트 씬 폴백이다.

**확정 값: 타일 스케일 1 + `TileSize` 6** (2026-08-04, sunjin1222·n0wst4ndup 합의). 이전은 같은 타일 아트를 2.5배 키워 `TileSize` 15였다.

**타일 아트가 스케일 1에서 정확히 6.00 × 6.00 유닛이다**(`RoadTile`·`GrassTile`·`LavaTile` 실측). 그래서 `TileSize 6`이 아트와 일치하는 값이고, **월드 단위로 authoring된 모든 값은 `6/15 = 0.4`를 곱해야** 타일 대비 비율이 유지된다. 배치 측 `tileSize`는 위 단일 출처로 자동 추종하지만 월드 단위 authoring 값은 추종하지 않으므로, 2026-08-04에 일괄 조정했다(아래).

**`TileSize` 15 → 6 일괄 조정 기록**

| 대상 | 처리 |
|---|---|
| 타워 프리팹 16개(본체·고스트·투사체) | 루트 스케일 **×0.4** — `CandyCanon`·`CandyCanon-Ghost`는 선행 조정돼 있어 건너뜀 |
| `TowerAsset` 9개 | `AttackRange`·`Radius`·`SplashRadius`·투사체 `Speed` **×0.4** |
| `EnemyAsset` 8개 | `MoveSpeed`·`AttackRange`·`ProjectileSpeed` **×0.4** |
| 씬 값 | `revealYOffset` 18→7.2 · `monsterWaypointYOffset` 8→3.2(WL-063) · `SkillManager.radius` 15→6 · `TowerPlacer.dummyAttackRange` 48→19.2 |

조정 후 실측 — **타일 15 시절 의도값으로 복귀했다.**

| | 조정 전 | **조정 후** | 타일 15 시절 의도 |
|---|---|---|---|
| 타워 크기 | 1.65 ~ 1.94칸 | **0.60 ~ 0.78칸** | 0.66칸 |
| 타워 사거리 | 5.0 ~ 15.0칸 | **2.00 ~ 6.00칸** | 2.0 ~ 6.0칸 |

⚠️ **스케일 대상이 아닌 것** — 헷갈리기 쉬운 항목이다.
- **`BaseProtectionRange`(5)**: `int`이고 `GrassEroder`가 **타일 거리**로 비교한다(`distance <= BaseProtectionRange`) → 월드 단위가 아니라 이미 타일 단위다.
- **배율·시간류**: `DamagePerSpeedUnit`·`SpeedFactor`·`AttackSpeedMul`·`HoldDuration`·쿨다운·간격.
- **`RangeCircle`·`TowerSpawnEffect`·`GrainSwarm`**: 사거리/`tileSize`를 인자로 받으므로 위 값이 바뀌면 자동 추종한다.
- **카메라 줌 범위(30~150)**: 룩 기준으로 별도 튜닝됨(`VisualLookPipeline.md` §3.7).

⚠️ **미처리 — 보스 패턴 임계값**(`Assets/Behavior/TankBossBehavior.asset`, #235 진행 중). 블랙보드 변수라 C# grep에 걸리지 않고 그래프 에셋에 **같은 변수가 여러 벌 직렬화**돼 있어 일괄 곱하면 조용히 어긋난다. `BossDesign.md`가 이미 "시드 3개에서 눈으로 확인 후 확정"을 요구하는 항목이므로 **소유자가 Behavior Graph 에디터에서 적용할 것** — 목표값은 `BossDesign.md`에 적었다.

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
- 배치 히트에서 `hit.collider.GetComponentInParent<BattleTile>()`로 타일을 얻는다. 셀 중심은 `tile.transform.position`이다.
- 배치 y는 `CalculateFootprintCenter`가 풋프린트의 `AnchorPosition.y` 최댓값을 기준으로 결정한다. 일반 타워는 최고 타일 윗면에 그대로 앉고, `AdaptiveTowerFoundation`이 있는 타워만 받침대 이음새를 가리기 위한 `FoundationSurfaceLift`를 더한다. 고스트와 실제 배치는 이 계산을 함께 사용한다.
- 높이차가 있는 풋프린트에서는 `AdaptiveTowerFoundation.Fit(lowestSurfaceY, highestSurfaceY)`가 최저·최고 타일 높이에 맞춰 받침대 높이와 위치를 조정한다.
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
                         Action<Transform> onConfirmed, Action onEnded = null);
```

- `onConfirmed`의 인자는 **방금 배치된 타워**다(#265). 좌표가 아니라 `Transform`인 이유: 합성 연출이 수렴 목적지로 결과 타워의 시각 중심(`CalculateVisualBounds`)을 재야 하고, 그 측정은 등장 연출이 스케일을 0으로 만들기 **전**이어야 한다.

- **반환값 = 배치 세션이 실제로 시작됐는가.** `false`면 `onEnded`도 **영영 오지 않는다** → 호출부가 배치 동안 유지하려던 상태를 걸어두면 안 된다는 신호다(합성이 이 신호로 커맨드를 즉시 `Undo`한다).
- `onEnded`는 확정/취소 **무관하게** 세션 종료 시 1회. **어느 쪽으로 끝났는지는 알려주지 않는다** — 구분이 필요한 소비처는 자기 상태로 판단해야 한다(`TowerMergeCommand`가 그렇게 한다).
- **프리뷰 사거리 해석**은 `TowerAsset.PreviewRadius` 단일 출처를 쓴다(WL-056/WL-079, #274). 공격 사거리와 두 오라 반경 중 **최댓값**이라 호출부가 타워 종류를 알 필요가 없다 — 예전에는 여기서 `TowerType`을 보고 분기했고 그것이 종류를 아는 4번째 지점이었다. 종류를 해석하지 않으므로 **"둘 다 없음" LogError 분기 자체가 사라졌다**(값이 0이면 원이 안 그려질 뿐이다).

> ⚠ **콜백 등록 순서**: `_onConfirmed`/`_onEnded`는 반드시 `MouseManager.BeginPlacement` **이후**에 대입한다. `BeginPlacement` 내부의 `CancelPlacement`가 **이전** 배치의 `OnEnded`를 발화해 두 필드를 소비·null 처리하기 때문이다. 먼저 대입하면 합성 재료 소모 콜백이 유실돼 **무료 합성**이 된다. 프리뷰 생성도 같은 이유로 `BeginPlacement` 이후다.

인스펙터의 더미 필드(`dummyGridWidth`/`dummyGridHeight`/`dummyAttackRange`)는 SO 경로가 들어오며 **사용되지 않는 잔재**다 — 제거 대상(§11).

---

## 7. 배치 흐름 (구현)

**`PlacementRequest`(히트 인지형)** — MouseManager는 그리드/스냅 규칙을 모르고 요청이 소유:
- `Func<RaycastHit, Vector3> Snap` (null이면 `hit.point`)
- `Func<RaycastHit, bool> CanPlaceAt`
- `Action<RaycastHit, Vector3> OnConfirmed`
- `Action OnEnded` — 취소/확정 복귀 시 프리뷰 정리(선택, null 허용)
- `Action OnRejected` — **놓을 수 없는 곳을 좌클릭**했을 때(무효 타일 + 맵 밖·하늘). 없으면 무효 클릭이 아무 신호도 없이 삼켜져 "클릭이 안 먹은 것"처럼 보인다. 현재 소비처는 거절 효과음
- `Action<bool> OnSurfaceHoverChanged` — **커서가 배치 표면 위인지 바뀔 때**(고스트를 켜고 끄는 것과 같은 타이밍). 아래 계약 참고

> ⚠️ **표면을 벗어나면 `Snap`이 아예 호출되지 않는다.** 매니저는 자기 고스트만 숨길 수 있고 요청이 따로
> 띄운 프리뷰(풋프린트 하이라이트·사거리 원)는 모른다. 그래서 예전에는 커서를 타일 밖으로 빼면
> **고스트만 사라지고 하이라이트·사거리 원이 마지막 타일에 그대로 남았다.** `OnSurfaceHoverChanged`가
> 그 구멍을 메운다 — 매니저는 고스트와 이 통지를 **같은 자리에서 함께** 처리하므로 둘이 어긋날 수 없다.
>
> 통지는 **상태가 바뀔 때만** 간다(매 프레임 아님). `TowerPlacer`는 `false`만 처리한다 — 켜는 일은
> 표면 위에서 매 프레임 도는 `Snap`이 하고(풋프린트 크기·앵커 유무를 아는 유일한 자리), 콜백은 `Snap`
> **뒤에** 실행되므로 거기서 또 켜면 여분 쿼드까지 되살아나 옛 자리에 남는다.

**흐름** (Idle/Placement 2상태, 새 상태 없음):
- 진입: 패널/버튼 → `BeginTowerPlacement(so)` → `StartPlacement` → `MouseManager.BeginPlacement`.
- 매 프레임(Placement): 레이캐스트 → `Snap(hit)`(풋프린트 중심) + 프리뷰 갱신 → 고스트 이동 → `CanPlaceAt(hit)`.
- 표면을 벗어난 프레임: 고스트 + 요청 프리뷰를 **함께** 숨긴다(`OnSurfaceHoverChanged(false)`), 좌클릭이면 `OnRejected`.
- 확정(좌클릭·유효): `OnConfirmed(hit, pos)` → 아래 확정 순서 → (`keepPlacing=false`면) Idle 복귀.
- 취소(우클릭) / 확정 복귀: `OnEnded` → 프리뷰 정리 → 종료 통지.

**확정 순서 (`PlaceTower`)** — 순서 자체가 계약이다:

1. 앵커 재취득 + `RebuildFootprint` + 전 셀 재검증 (프레임당 1회라 캐시를 믿지 않고 재확인)
2. `ManagementController.TrySpend(_activeCost)` — 실패 시 배치 취소 (씬에 없으면 무료 = 테스트 씬)
3. `Instantiate(towerPrefab, snappedPos, identity)`
4. `TowerFootprint` 부착 + 전 셀 `Occupy` — 인스턴스가 점유를 기억해야 파괴 시 되돌릴 수 있다
5. `TowerGroupSelectable` 부착 — 합성 재료 후보로 등록(결과 타워도 이 경로라 다단 합성 가능)
6. **`TowerTileBuff.Initialize(...)`** ← **반드시 `Tower.Build` 앞**
7. `Tower.Build(_activeAsset)` — **패널에서 산 SO가 프리팹이 문 SO를 이긴다**(WL-129). 다르면 경고 후 산 쪽으로 재조립. `Tower` 없는 프리팹은 LogError
8. **`TowerPlaceCommand` 인수 + `Execute()`** — 되돌리기 커맨드가 방금 선 타워와 실지불 비용을 넘겨받는다(#281). 배치를 **수행하지 않고 인수만 한다** — 실패해도 배치는 정상이고 "되돌릴 수 없다"만 잃으므로 경고로 드러내고 진행
9. `onConfirmed(command)` 1회 실행 (먼저 비우고 호출 → `keepPlacing`에서 재실행 방지)
10. **히스토리 등록** — `_historyOwner == PlacementOwner.Placer`일 때만 `CommandHistory.Push(command)`
11. **`TowerSpawnEffect.Play(...)`** — 로직이 전부 끝난 뒤 마지막 (§9.3)

> **9 → 11 순서도 계약이다**(#265): 합성 연출이 9단계에서 결과 타워의 `bounds`를 재 수렴 목적지로 삼는데, 11단계가 시작하는 즉시 그 타워의 스케일이 0이 된다. 뒤집으면 입자가 쪼그라든 상자의 중심으로 모인다. 콜백 인자가 `Transform`에서 `TowerPlaceCommand`로 바뀌었지만(#281) 연출이 쓰는 값은 `command.Placed`로 같으므로 이 계약은 그대로다.

> **10단계의 소유권 신호가 필요한 이유**(#281): 일반 배치와 합성 결과 배치가 **같은 `PlaceTower`를 공유**한다. 무조건 등록하면 합성 결과에 `TowerPlaceCommand`와 `TowerMergeCommand`가 둘 다 올라가 한 번의 합성이 두 번에 나눠 되감긴다(중간에 결과도 재료도 없는 빈 타일이 한 번 보인다). 그래서 합성은 `PlacementOwner.Caller`로 열고 결과 커맨드를 `TowerMergeCommand.AdoptResult`로 편입한다. **`onConfirmed != null` 같은 암묵 판정을 쓰지 않는다** — 확정 콜백을 쓰는 세 번째 소비처가 생기는 순간 이중 등록/미등록으로 조용히 갈린다. `historyOwner`에 기본값이 없는 것도 같은 이유다.

> **소유권은 확정 콜백과 같은 1회성 값이다**(#281): 10단계 직후 `_historyOwner`를 `PlacementOwner.Placer`로 되돌린다. `keepPlacing`(WL-105)이 켜지면 한 세션에서 `PlaceTower`가 여러 번 도는데, 그때 `Caller`가 남아 있으면 2번째 이후 클릭이 만드는 결과 타워 복제분이 `AdoptResult`도 `Push`도 받지 못해 **영구히 되돌릴 수 없다.** 복제분은 재료를 쓰지 않고 `ExtraCost`만 지불한 별개 배치이므로 각자 독립 커맨드로 히스토리에 오르는 것이 옳다. `OnEnded`의 리셋만으로는 못 막는다 — 그건 세션이 끝날 때만 돌고, 이 문제는 세션 안에서 벌어진다.

> **6 → 7 순서를 뒤집으면 안 되는 이유**: 버프 오라는 **조립 시점에 자기 반경으로 대상을 한 번 훑는데**, 그 반경이 타일 버프(사거리)에 의존한다. 순서가 뒤바뀌면 첫 적용이 버프 이전 반경으로 계산된다. 구 `AuraTower`가 `Start`에서 반경을 재계산하던 우회로가 정확히 이 문제였고, 여기서 순서를 정해 그 우회로를 없앴다.

**TowerPlacer 판정**:
- `Snap`: 앵커 기준 풋프린트 **중심**으로 스냅(y = 타일 앵커). 앵커가 바뀐 프레임에만 풋프린트를 재구성하고 사거리 원을 갱신한다.
- `CanPlaceAt`: 풋프린트 전 셀이 `Grass && !Occupied` + `CanAfford`. **`Snap`이 채운 캐시를 신뢰한다**(MouseManager가 매 프레임 Snap → CanPlaceAt 순으로 호출).
- `OnEnded`: 프리뷰 정리 → `_onConfirmed`·`_historyOwner` 폐기(취소로 끝났으면 재료 보존) → 종료 통지를 **먼저 비우고** 호출(구독자가 그 안에서 새 배치를 시작해도 중복 발화 없음).

**전제(와이어링)**: 타일이 `_placementMask`(Ground) 레이어 + Collider 보유, 씬에 `MouseManager` 존재, `TowerAsset`에 tower/ghost 프리팹 지정(고스트는 Collider 없음), `TowerPlacer.tileBuffRules`에 `TileBuffRuleSettings` 지정.

---

## 8. 통합 훅 (자원 · 페이즈)

| 훅 | 상태 | 구현/잔여 |
| --- | --- | --- |
| **자원 차감** | ✅ 구현 | `ManagementController.CanAfford`(검증) / `TrySpend`(확정). 비용 출처 = `TowerAsset.Cost`. 컨트롤러가 씬에 없으면 **무료 배치**(경영 없는 테스트 씬 지원) |
| **낮 전용 게이팅** | ❌ 미구현 | `TowerPlacer` 진입에 `DayNightManager.CurrentPhase` 확인이 없다. 완화만 존재 — `PhasePanelSwitcher.ShowNight`가 밤 진입 시 진행 중 배치를 취소한다. 합성 실행부 축은 **WL-077**(muchan) |
| **되돌리기**(#281 → #444) | ✅ 구현 | 확정 시 `TowerPlaceCommand`를 `CommandHistory`에 올린다(LIFO 20). 되돌리면 타워를 파괴하고 `ManagementController.Grant`로 **실지불 비용을 100% 환원**한다. `OnDayToNight`에 히스토리 전체가 `Commit`돼 되돌리기 불가로 확정된다. **#444**: 상태 기계·비용 환원이 공통 기반 `ReversibleCommandBase`로 올라가고 **경영 조작(건물 업그레이드 · 주민 증축)이 같은 스택에 합류**했다 — 되돌리기 요청은 버튼·Ctrl+Z 모두 `UndoRequest.Submit()`을 지나며, 파괴되는 대상이 있어 선택을 푸는 일은 `TowerPlaceCommand.OnUndo`가 **확정본일 때만** 한다(요청 진입점에서 무조건 풀면 건물 패널이 닫힌다) |

> 자원은 `ResourceWallet`에 직접 접근하지 않고 **반드시 `ManagementController` 경유**다(WL-017). `TrySpend`는 원자적 — 전부 감당 가능할 때만 전부 차감한다. 환원(`Grant`)은 그 대칭짝이며, 인자는 반드시 **커맨드가 들고 있는 실지불 비용**이어야 한다(임의 수량 지급 금지 — 팀 계약 #3·#6).

> **되돌리기가 타일을 되찾는 순서**(#281): 되돌린 타워는 `Object.Destroy` **전에** `TowerFootprint.Release()`를 명시적으로 부른다. `Destroy`는 프레임 끝까지 지연되므로 그러지 않으면, 합성 되돌리기가 곧바로 재료를 `Reoccupy`할 때 그 타일이 아직 점유 상태로 보여 `TowerFootprint`가 소유권을 포기한다(재료가 타일 없는 타워로 되살아난다).

---

## 9. 시각 피드백

### 9.1 사거리 미리보기 (구현)

배치 중 고스트 위치에 `RangeCircle`(공용 컴포넌트)로 **채움 + 굵은 외곽선** 원을 표시한다. `rangeFillColor`(반투명) / `rangeColor`(외곽선).

- 첫 스냅 전에는 `Hide()` — 그러지 않으면 맵 원점(0,0)에 원이 노출된다.
- **커서가 배치 표면을 벗어나면 숨긴다**(`OnSurfaceHoverChanged(false)`). 고스트만 숨기던 시절 이 원이 마지막 타일에 잔류했다.
- **앵커가 없으면(배치 마스크는 맞혔지만 `BattleTile`이 아닌 표면) 숨긴다.** 무조건 `Show`하면 놓을 수 없는 곳에서 원이 커서를 따라다닌다.
- 반경은 **타일 버프를 반영한 프리뷰 값**이다: `(기본사거리 + Flat) × (1 + Percentage/100)`. 앵커가 바뀐 프레임에만 재계산한다.
- 같은 반지름이면 `RangeCircle`이 지오메트리 재생성을 생략한다.

**부모 스케일 역보정은 매 표시마다 재계산한다**(`Show`/`SetRadius`/`LateUpdate`). 생성 1회 캡처는 그 순간의 부모 스케일에 영원히 고정되는데, 등장 연출(§9.3)이 타워 루트를 0→원본으로 애니메이션하면서 **과도기에 원이 생성되는 경로가 실제로 생겼다** — 배치 직후 팝 구간에 그 타워를 드래그 선택하면 `Tower.ShowRangeCircle`이 거기서 원을 만들고 캐시하므로, 보정이 예컨대 1/0.41≈2.44로 굳어 그 타워의 사거리 원이 **이후 계속 2.44배**로 표시된다(#192가 막으려던 증상의 재발 경로). 부모 스케일이 1로 고정된 대상에서는 동작이 이전과 동일하다.

### 9.2 풋프린트 셀 하이라이트 (구현)

풋프린트 각 셀에 바닥에 눕힌 반투명 쿼드를 **유효=`validCellColor`(초록) / 무효=`invalidCellColor`(빨강)** 로 표시한다. 표시 y는 타일 윗면 + 0.03(z-파이팅 방지). 고스트 자체의 유효/무효 색은 이 하이라이트로 대체돼 별도 불필요.

**표시/숨김의 주체가 갈려 있다** — 잔상 버그가 전부 이 경계에서 났다:

| 상황 | 처리 |
|---|---|
| 생성 직후(첫 스냅 전) | **꺼진 채로 만든다**. 사거리 원을 `Hide`로 시작하는 것과 같은 이유 — 배치 버튼이 UI 위에 있어 클릭한 프레임의 커서는 타일 위가 아니고, 첫 스냅 전까지 위치가 갱신되지 않아 **월드 원점에 쿼드가 보인다** |
| 표면 위 | `UpdateCellHighlights`가 매 프레임 위치를 잡으면서 **켠다**. 켜는 주체가 위치를 잡는 주체와 같아야 "켜졌는데 위치가 옛날"이 생기지 않는다 |
| 풋프린트보다 많은 여분(앵커 없음 → `_footprint`가 빔 포함) | 같은 함수가 **끈다**. 켠 채로 두면 위치를 갱신받지 못한 쿼드가 옛 자리에 남는다 |
| 표면을 벗어남 | `OnSurfaceHoverChanged(false)`가 **전부 끈다**(§7). 이때는 `Snap`이 아예 돌지 않아 위 경로가 작동하지 않는다 |

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
void      Play(Transform target, float footprintSize, float tileSize);                    // fire-and-forget
UniTask   PlayAsync(Transform target, float footprintSize, float tileSize, CT ct);        // 종료까지 대기
Bounds    CalculateVisualBounds(Transform target, float footprintSize);                   // 공용
const float ConvergeDuration = 0.45f;   // 합성 소모 연출(#265)과 공유하는 시간 축
const float PopDuration      = 0.28f;   // 〃
```

두 상수가 public인 이유는 **합성 유입 입자가 등장 팝보다 늦게 도착하면 안 되기 때문**이다. 비행 시간을 거리가 아니라 이 값으로 묶으면 재료가 얼마나 멀든 그 안에 도착한다 — 속도를 고정하면 먼 재료의 입자가 타워가 다 선 뒤에 도착해 인과가 뒤집힌다. (상한 안에서는 알갱이 크기가 각자의 도착 시각을 정하고, 가장 큰 알갱이가 이 값을 꽉 채운다.) 상세는 `TowerMerge.md` §9.2.2.

알갱이 자체(`GrainSwarm`)와 스케일 점유(`VfxScaleHold`)도 두 연출이 공유하는 부품이다. 여기 남은 것은 **이 연출 고유의 움직임**(후광 분포 · 소용돌이 수렴 · 바닥 링)뿐이다.

진입점이 받는 것은 `Transform`과 **그리드가 정하는 길이 둘**(풋프린트·타일)뿐이다. `Tower`도 `TowerAsset`도 메시도 받지 않고, 대상에서 읽는 것은 **`Renderer.bounds`와 `localScale`이 전부**다. 둘이 각각 무엇을 정하는지는 아래 앵커 표를 따른다.

> ### ⚠ 이 연출은 대상 루트의 `localScale`을 **배타적으로 소유**한다
>
> 읽기만 하는 게 아니라 **쓴다** — 시작 시 0으로 덮고, 등장 구간(back-out)을 거쳐 캡처한 원본으로 되돌린다. 대상이 안 보이는 창은 수렴 구간 약 **0.45초**이고, 스케일이 과도기 값인 창은 등장 구간 약 **0.28초**다.
>
> 이 한 문장이 아래 세 가지의 공통 뿌리다. 새 소비처를 붙이기 전에 반드시 확인할 것:
>
> | 파생 문제 | 현재 방어 |
> | --- | --- |
> | 같은 대상에 두 번 재생 → 두 번째가 **0을 원본으로 캡처** → 타워 영구 투명 | `VfxScaleHold`(대상에 붙는 점유 컴포넌트). 이미 점유 중이면 **그 자리에서 원본을 복원하고 인계**하며, 인계당한 연출은 점유 세대(`IsSuperseded`)를 보고 입자·링까지 스스로 접는다. 점유 상태가 대상과 함께 죽으므로 연출 도중 타워가 사라져도 남는 것이 없다(구 static 딕셔너리에서 바뀐 점) |
> | 과도기 스케일을 다른 시스템이 **캡처해 굳힘** | `RangeCircle`이 부모 스케일 역보정을 생성 1회가 아니라 표시할 때마다 재계산(§9.1) |
> | 대상 루트 스케일을 쓰는 **`Animator`** 가 새 에셋에 붙으면 서로 덮어씀 | **방어하지 않는다** — 눈에 즉시 보이는 실패라 발생 시 시각 자식만 스케일하는 형태로 바꾼다 |
>
> 콜라이더도 이 창 동안 함께 죽는다. 단 **드래그 선택은 콜라이더가 아니라 위치 기반**(`MouseManager.RefreshBoxHits`)이라 스케일 0인 타워에도 도달한다 — "연출 중엔 선택이 안 된다"고 가정하면 안 된다.
>
> 공격은 문제가 되지 않는다. `AttackAction.ActivePhase == NightOnly`이고 `Tower.Update`가 낮이면 Tick을 건너뛴다.

**왜 이렇게까지 하는가**: 타워 에셋이 임시라 통째로 교체될 예정이기 때문이다. 연출이 특정 메시·머티리얼·계층 구성을 조금이라도 참조하면 교체와 함께 깨진다. 그 결과 **대상이 타워일 필요조차 없어서** Renderer가 달린 큐브에 그대로 재생되고, 에셋 없이 연출을 튜닝·검증할 수 있다.

**수치 앵커를 둘로 나눈다. 이게 설계의 전부다:**

| 앵커 | 무엇을 정하나 | 왜 |
| --- | --- | --- |
| **타일 한 칸**(그리드 최소 단위) | 알갱이 크기(+ 화면 하한·상한) | 타워 메시는 제각각이라(**현재 프리팹만 봐도 높이 2.0~37.7, 19배**) bounds에 묶으면 스케일이 어긋난 프리팹에서 알갱이까지 어긋난다. 타일은 그리드가 정하는 값이라 **에셋 교체와 무관하게 불변**이고, 덕분에 **모든 타워의 알갱이가 같은 크기**로 보여 하나의 시각 언어가 된다 |
| **풋프린트**(= 칸 수 × 타일, 논리 크기) | 바닥 링 반경 · 후광 두께 | "이 타워가 몇 칸을 먹었다"를 말하는 값이라 칸 수에 비례해야 한다 |
| **bounds**(시각 크기) | 입자 개수 · 구름 모양 | 큰 타워는 알갱이가 많아야 하고(30~90), 후광은 **그 타워의** 실루엣을 감싸야 한다 |

> **한 문장 규칙**: **알갱이는 칸(타일), 바닥·후광은 자리(풋프린트), 분포·개수는 시각(bounds).** 새 요소를 추가할 때 위 표를 재해석하지 말고 이 문장을 따를 것.

> ⚠ **타일과 풋프린트를 한 인자로 겸하지 말 것**(#265 리뷰에서 정정). 1×1 타워에서는 두 값이 같아 겸용이 오래 들키지 않는데, 다중 셀 타워가 들어오는 순간 **알갱이 크기만 조용히 어긋난다** — 1×1 재료가 2×2 결과로 합쳐지면 유입 입자가 등장 후광의 절반 크기가 되어, 두 연출이 같은 물질로 보여야 한다는 #265의 전제가 깨진다. 그래서 `Play`가 길이 인자를 **둘** 받는다.

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
| 알갱이 크기 | 타일 × 0.15 (=2.25) | 게임 줌 70·1080p에서 17.4px |
| 알갱이 화면 하한 / 상한 | `orthoSize × 0.017` / 타일 × 0.4 | 줌 300에서도 보이게 / 칸을 뒤덮지 않게. 줌 전 구간 확인됨 |
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

#### 9.3.5 #265(합성 소모 연출)와의 관계 — 구현됨

**#265는 이 연출을 호출하지 않는다.** 설계 단계에서는 "합성이 `PlayAsync`를 마지막 구간으로 재사용한다"였지만, 그러면 `TowerPlacer`가 확정 말미에 부르는 `Play`와 겹쳐 **같은 대상에 두 번 재생**된다. 실제 구현은 그 대신 **부품과 시간 축만 공유**한다:

| 공유물 | 내용 |
| --- | --- |
| `GrainSwarm` | 알갱이의 시각적 정체성(빌보드 쿼드·절차 텍스처·개수/크기 규칙·전체 알파). 움직임은 각 연출이 정한다 |
| `VfxScaleHold` | 대상 스케일 배타 점유 + back-out 팝 + 원복. 두 연출의 "뽕!"이 같은 구현이다 |
| `ConvergeDuration` / `PopDuration` | 시간 축. **합성 입자는 거리와 무관하게 이 시간 안에 도착**하므로 등장 팝보다 늦지 않는다(가장 큰 알갱이가 꽉 채운다) |
| `CalculateVisualBounds` | 재료의 크기와 결과 타워의 수렴 목적지를 **같은 규칙으로** 잰다 |

그 결과 두 연출은 화면에서 구분되지 않으면서도 서로의 스케일을 건드리지 않는다. 상세는 `TowerMerge.md` §9.2.

---

## 10. 인수 조건 (Acceptance Criteria)

- [x] 고스트가 풋프린트 중심으로 스냅된다.
- [x] road·lava·타일없음·점유 셀이 풋프린트에 포함되면 무효(빨강 하이라이트), 좌클릭 무반응.
- [x] 풋프린트 전 셀이 grass·미점유면 유효(초록), 좌클릭 시 타워가 중심에 생성되고 전 셀 점유.
- [x] 점유된 셀에 겹쳐 배치 불가.
- [x] 우클릭으로 취소, 프리뷰 정리.
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
- **WL-001 (PARTIAL)** — `lightning_tower`(`Impact=Chain`)만 Attack/ChainRadius/MaxChainTargets가 전부 0이라 **배치해도 무동작**이다. ⚠ `TowerPrefab`/`GhostPrefab`이 둘 다 null이라 저작 검증(`TowerAsset.OnValidate`)도 이 SO에는 **침묵한다** — 프리팹이 없으면 검증을 건너뛰기 때문이다.
- **WL-005 (PARTIAL)** — 대상 탐지를 LayerMask로 할지 Tag로 할지 미확정.

**연출(§9.3) — 검증은 끝났고(§9.3.4), 남은 것은 "할 일"이라기보다 "열려 있는 결정"이다**
- **아트 방향 확정 시 룩 전면 재검토.** 색·형태·지속 시간 어느 것도 고정이 아니다. 유지해야 하는 것은 §9.3.2 계약뿐이고, 나머지는 갈아엎어도 된다.
- **타워 에셋이 실제로 교체될 때 재확인** — 크기가 크게 다른 대상으로 한 회귀는 통과했지만(§9.3.4), 실 교체 시점에 한 번 더 재생해 보는 것이 싸다. **코드 수정이 필요해지면 계약이 깨진 것이다.**
- ~~**#265 합성 소모 연출**~~ — 구현됨. 이 함수를 재사용하는 대신 부품·시간 축을 공유하는 형태가 됐다(§9.3.5).

**해소됨** (`WatchList-Archive.md`)
- ~~WL-004 (배치 검증 공백)~~ — `BattleTile` + `TowerPlacer`로 해소.
- **WL-007 (좌표 이원화) — 재개.** "배치 측은 변환하지 않는다"가 사실이 아니었다: 이웃 셀·중심 오프셋·하이라이트 쿼드 회전이 곧 셀→월드 변환이고, 그 사본이 월드축을 가정해 **맵 회전(Y 59.45°)에서 실제로 깨졌다**(§3 좌표, 2026-08-04 수정). 지금은 타일 루트(`CoordinateRoot`)를 기준축으로 받아 축의 출처는 통일했지만, **위치 변환식 자체는 `CombatMapTileSpawner`(로컬 공간 + `TransformPoint`)와 여전히 별개 구현**이다 — WL-034의 해소안(공용 셀↔월드 변환 유틸)이 남은 정답이다.
- ~~**타워/고스트 회전 미결**~~ → **해소(2026-08-04).** 셀 하이라이트·타워 본체·고스트가 모두 그리드 축(`GridBasis`)을 따른다. 고스트는 `PlacementRequest.GhostRotation`으로 전달한다 — §3 좌표.
- **`TileSize` 15 → 6 잔여: 보스 패턴 임계값**(§3.1의 미처리 항목) — `TankBossBehavior.asset` 블랙보드 변수를 소유자가 그래프 에디터에서 ×0.4 적용해야 한다(#235).
- ~~WL-129 (산 SO ≠ 배치된 SO)~~ — `Tower.Build(_activeAsset)`로 해소(§7-7).

**기타**: 점유 수명주기는 타일별 플래그 + `TowerFootprint.OnDestroy`라 맵 리셋(타일 파괴/재생성) 시 자동 초기화된다.

---

## 12. 확장 여지

- **가변 풋프린트**: 코드는 W×H 지원(구현됨). 현재 CSV 데이터가 전부 1×1이라 시각적으론 1×1 — designer가 CSV GridWidth/Height를 키우면 즉시 확대. (타워 프리팹 자체의 W×H 비주얼은 별도.)
- **철거/판매·재배치**: `TowerFootprint.Release` + 자원 환급. 점유 해제 API는 합성(#263)이 이미 만들어 뒀다.
- **허용 규칙 강화**: '도로 인접만' 또는 맵빌더 '건설가능' 타일 명시(Q1 대안).
- **지원 타워**(haste_tower 등, WL-026): 인접 셀 버프 — 배치 규칙엔 영향 없음.
- **연출 재사용**: `TowerSpawnEffect`는 타워를 모르므로 건물 건설·영토 확장 등 "무언가 등장하는" 다른 자리에도 그대로 쓸 수 있다. 다만 지금 룩이 임시라 재사용을 전제로 설계를 굳히지는 말 것.
