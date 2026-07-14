# 전투 공간 타워 배치 — 기능 명세

> **상태**: 핵심 배치 코어 구현 + 테스트 씬 검증 완료 · **실 타워/SO·자원 연동 예정**
> **소유**: n0wst4ndup(배치 흐름·게이트웨이·프리뷰) · SUNGSOO(타워 프리팹) · muchan(타워 데이터·자원)
> **구현 파일**:
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/BattleTile.cs` (타일 마커)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerPlacer.cs` (게이트웨이·배치 코어)
> - `Assets/Scripts/GameManager/MouseManager/PlacementRequest.cs` (계약: Snap/CanPlaceAt/OnConfirmed/OnEnded, 히트 인지형)
> - `Assets/Scripts/GameManager/MouseManager/MouseManager.cs` (Snap 위임·히트 전달·OnEnded 발화)
> - `Assets/Scripts/GameManager/MouseManager/Helper/PlacementButton.cs` (테스트 헬퍼)
> **관련**: GDD §5.1·§6.2, MouseManager #9, 통합 #71, WL-001/005/009
> **참조**: `Docs/Core/MouseManager.md`, `Docs/BattleMapBuilder/BattleMapBuilder.md`, `Docs/Review/SystemMap.md`
> 코드가 이 명세와 어긋나면 문서를 갱신한다(팀 계약 #7).

---

## 0. 설계 요지

- **배치 측(n0wst4ndup)이 구현하고 BattleMapBuilder는 변경하지 않는다.** 맵은 타일을 셀 중심에 GameObject로 스폰하므로, **배치 레이캐스트가 맞힌 타일 GameObject가 곧 셀**이다(셀 중심 = 그 타일의 `transform.position`). → 월드↔셀 좌표 변환 **없음**(WL-007 회피).
- **타일 종류·점유는 타일에 붙인 `BattleTile` 마커**로 안다(맵빌더 질의 API 불요).
- **타워 데이터(프리팹·풋프린트·사거리)는 게이트웨이로 주입한다.** 지금은 더미(인스펙터), 나중에 **SO를 주입**받아 대체한다. TowerPlacer는 특정 SO에 결합하지 않는다(§6).

---

## 1. 목적

낮(경영) 페이즈에 플레이어가 **전투 공간 그리드의 허용된 셀**에 타워를 배치하는 상호작용. 밤에 타워는 사거리 내 적을 자동 공격하지만(GDD §5.2), 본 명세는 **배치까지**만 다룬다. 구현상 MouseManager의 두 미구현 지점(`Snap` 항등, `CanPlaceAt` 항상 true)을 타일 마커 기반 스냅·검증으로 실체화한 것이다.

---

## 2. 범위

**In (구현됨)**
- "허용 셀"(건설 가능) 판정 규칙 (§4)
- 고스트 스냅(풋프린트 중심) → 유효/무효 → 확정 배치 (`PlacementRequest` 히트 인지형, 새 상태 없음)
- 타일 종류·점유 식별 (`BattleTile` 마커)
- **W×H 풋프린트** 점유 (타워별 GridWidth×GridHeight, 타일별 bool 다중 셀)
- **사거리 미리보기** (배치 중 런타임 원)
- **풋프린트 셀 하이라이트** (셀별 유효/무효 색)
- 데이터 **게이트웨이 진입 구조** (더미 ↔ SO 주입 교체 지점 — §6)

**Out (훅/예정)**
- 자원 차감·낮 전용 게이팅 → §8 훅 (통합 #71에서 연결)
- 실 타워 프리팹 + 데이터 SO 주입 → §6 게이트웨이(예정)
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

**좌표 (WL-007)**: 배치 측은 월드↔셀 변환을 하지 않는다. 히트한 **타일 GameObject를 직접** 다루고, 풋프린트의 이웃 셀은 `tileSize`(=StageBuilder.TileSize=5) 간격으로 앵커 주변 지점을 공간 질의(`OverlapSphere`)해 찾는다. **그리드가 월드 X/Z축에 정렬돼 있다고 가정**한다(battlespace 회전 없음).

---

## 4. 허용 위치 규칙

배치 가능 = 앵커(히트 타일) 기준 **W×H 풋프린트의 모든 셀**이 `BattleTile`을 갖고 **`Kind == Grass` && `Occupied == false`**. 확정 시 풋프린트 전 셀을 점유한다.

셀 하나의 판정이 함의하는 것:
- **인맵**: 타일 존재 ⇒ 생성된 블록 안. 맵 밖이면 `BattleTile` 없음 → 무효.
- **비도로·비용암**: 스폰된 타일은 grass/road/lava 중 정확히 하나. Grass ⇒ 도로도 용암도 아님.
- **비특수**: 스폰 지점·최종 목표는 경로(도로) 위 또는 별도 오브젝트 → Grass 아님 → 자동 제외.
- **미점유**: `!Occupied`.

> **결정(Q1)**: 별도 '건설가능' 타일 지정 없이 **grass 타일 = 건설 가능**. W/H는 타워 데이터의 GridWidth/GridHeight(현재 더미; 실데이터는 muchan CSV, 5종 모두 1×1).

---

## 5. 시스템 책임 분담

| 단계 | 소유 | 비고 |
| --- | --- | --- |
| 포인터 입력·레이캐스트·고스트·상태·**히트 전달** | **MouseManager** | 그리드 규칙 무지(제네릭). 계약 #1·#6 |
| 게이트웨이 진입·스냅·풋프린트 검증·생성·점유·프리뷰 | **TowerPlacer** (n0wst4ndup) | 배치 규칙 전부 여기 |
| 타일 종류·점유 데이터 | **BattleTile** 마커 | 타일에 부착 — §6 |
| 타워 프리팹·스탯·생성물 | **Combat** | `towerPrefab`(현재 더미, 예정: SO 제공) |
| 자원 차감 | **Management** (`ResourceWallet.TrySpend`) | §8 훅(통합 #71) |
| 정보 패널 | **UI** (`TowerInfoUI`) | WL-011 |
| **BattleMapBuilder** | **코드 변경 없음** | 타일이 `BattleTile`을 갖게 하는 건 와이어링(§6) |

---

## 6. 메커니즘 (타일 마커 + 데이터 게이트웨이)

### 6.1 타일 식별 (`BattleTile` 마커)
- `BattleTile { TileKind Kind; bool Occupied }` — 데이터 전용 컴포넌트.
- 배치 히트에서 `hit.collider.GetComponentInParent<BattleTile>()`로 타일을 얻는다. 셀 중심 = `tile.transform.position`, 점유 = `tile.Occupied`.
- 풋프린트 이웃 셀은 앵커 위치에서 `tileSize` 간격으로 계산한 지점을 `Physics.OverlapSphere(cell, tileSize*0.4f)`로 조회해 각 `BattleTile`을 찾는다.

**타일 태깅 (완료)**: 전투 타일 프리팹에 `BattleTile`(Kind 설정)이 **부착돼 있다** — 인스턴스는 `Instantiate` 시 이를 그대로 가지므로 `StageMapSpawner`의 별도 태깅이 필요 없다. 단 프리팹이 **`Assets/Imported/`(벤더링, 별도 git 저장소)**에 있어 **메인 repo diff·자동 리뷰에는 이 부착이 보이지 않는다**(팀은 Imported를 별도 git로 공유). → `StageBuilder`의 grass/road/lava 필드가 이 태깅된 프리팹을 가리켜야 한다는 것이 유일한 전제.

### 6.2 데이터 게이트웨이 (더미 → SO 주입)
TowerPlacer가 배치에 쓰는 값은 `TowerPlacementData { GridWidth, GridHeight, AttackRange }` + 프리팹(tower/ghost)이다. TowerPlacer는 특정 SO에 결합하지 않고, **진입 방식과 무관한 코어 `StartPlacement(TowerPlacementData)`** 를 둔다.

- **현재 진입**: `BeginTowerPlacement()` — 인스펙터 더미 값 + SerializeField 프리팹으로 `StartPlacement` 호출(테스트 경로).
- **예정 진입(게이트웨이)**: tower/ghost 프리팹 + 풋프린트/사거리를 담은 SO가 생기면 아래 오버로드를 추가한다. 코어는 무수정:
  ```
  public void BeginTowerPlacement(TowerXxxSO so) {
      towerPrefab = so.TowerPrefab; ghostPrefab = so.GhostPrefab;
      StartPlacement(new TowerPlacementData(so.GridWidth, so.GridHeight, so.Range));
  }
  ```

---

## 7. 배치 흐름 (구현)

**`PlacementRequest`(히트 인지형)** — MouseManager는 그리드/스냅 규칙을 모르고 요청이 소유:
- `Func<RaycastHit, Vector3> Snap` (null이면 `hit.point`)
- `Func<RaycastHit, bool> CanPlaceAt`
- `Action<RaycastHit, Vector3> OnConfirmed`
- `Action OnEnded` — 취소/확정 복귀 시 프리뷰 정리(선택, null 허용)

**흐름** (Idle/Placement 2상태, 새 상태 없음):
- 진입: 버튼 → `BeginTowerPlacement()` → `StartPlacement` → `MouseManager.BeginPlacement`.
- 매 프레임(Placement): `_placementMask` 레이캐스트 → `Snap(hit)`(풋프린트 중심) + 프리뷰(원·셀 하이라이트) 갱신 → 고스트 이동 → `CanPlaceAt(hit)`.
- 확정(좌클릭·유효): `OnConfirmed(hit, pos)` → **풋프린트 전 셀 점유** + 중심에 타워 생성 → (`KeepPlacing=false`면) Idle 복귀.
- 취소(우클릭/Esc) / 확정 복귀: `OnEnded` → 프리뷰 정리.

**TowerPlacer 판정**:
- `Snap`: 앵커 기준 풋프린트 **중심**으로 스냅(높이는 히트 표면). 사거리 원·셀 하이라이트를 여기서 갱신.
- `CanPlaceAt`: 풋프린트 전 셀이 `Grass && !Occupied`. [+ §8 훅]
- `OnConfirmed`: 재확인 후 `towerPrefab` 생성, 전 셀 점유. [+ §8 훅]
- `OnEnded`: 사거리 원·셀 하이라이트 제거.

**전제(와이어링)**: 타일이 `_placementMask`(Ground) 레이어 + Collider 보유, `tileSize`가 실제 타일 간격과 일치, `TowerPlacer`에 `towerPrefab`/`ghostPrefab` 지정(고스트는 Collider 없음), 씬에 `MouseManager` 존재.

---

## 8. 통합 훅 (자원 · 페이즈) — 통합 #71에서 연결

배치·검증 흐름만 구현하고, 아래는 `TowerPlacer`에 **훅 주석만** 있다(Q3).

- **자원 차감**: `OnConfirmed`(및 필요 시 `CanPlaceAt`)에서 `ResourceWallet.TrySpend(kind, cost)` 성공 시에만 생성·점유. 비용 출처 = `TowerAsset.Cost`(muchan) — Combat 미소비(WL-001).
- **낮 전용**(계약 #5): 진입 시 `DayNightManager.CurrentPhase` 확인(`Instance` null 체크).

---

## 9. 시각 피드백 (구현)

- **사거리 미리보기**: 배치 중 고스트 위치에 런타임 `LineRenderer` 원(반경 = 데이터 사거리)을 표시. `OnEnded`로 정리. (색은 `rangeColor`.)
- **풋프린트 셀 하이라이트**: 풋프린트 각 셀에 바닥에 눕힌 반투명 쿼드를 셀별로 **유효=`validCellColor`(초록) / 무효=`invalidCellColor`(빨강)** 로 표시. → 고스트 자체 유효/무효 색은 이 하이라이트로 대체돼 별도 불필요.
- 후속: 하이라이트/원 아트 폴리싱, 무효 사유별 구분.

---

## 10. 인수 조건 (Acceptance Criteria)

> 전제: 전투 타일 프리팹에 `BattleTile` 부착됨(§6.1 — 완료).

- [x] 고스트가 풋프린트 중심으로 스냅된다.
- [x] road·lava·타일없음·점유 셀이 풋프린트에 포함되면 무효(빨강 하이라이트), 좌클릭 무반응.
- [x] 풋프린트 전 셀이 grass·미점유면 유효(초록), 좌클릭 시 타워가 중심에 생성되고 전 셀 점유.
- [x] 점유된 셀에 겹쳐 배치 불가.
- [x] 우클릭/Esc로 취소, 프리뷰 정리.
- [x] 사거리 미리보기 원이 데이터 사거리로 표시된다.
- [ ] (훅) 자원 부족 시 확정 실패 / 밤 진입 차단 — 통합 #71.

검증: 개인 테스트 씬 Play 확인(팀 컨벤션 — 유닛 테스트 없음). 시각 검증은 씬 뷰 스크린샷(`Docs/Tools/unity-cli-guide.md` §4.J).

---

## 11. TODO / 의존

- **SO 게이트웨이(예정)**: tower/ghost 프리팹 + 풋프린트/사거리를 담은 SO + `BeginTowerPlacement(SO)` 오버로드(§6.2). SO는 후속 작성.
- **자원·페이즈 훅**(§8): 통합 #71에서 연결.
- **타일 `BattleTile` 태깅 — 완료**(§6.1): 전투 타일 프리팹에 부착됨(Imported·별도 git이라 메인 repo diff엔 안 보임). `StageBuilder`가 태깅된 프리팹을 가리키는 것이 전제.
- **WL-004 (배치 검증 공백) — 해소**(이 PR): CanPlaceAt/Snap/타일 종류 판정을 TowerPlacer + BattleTile로 구현. MapBuilder 그리드 API는 불요.
- **WL-032 (신규 — `tileSize` 이중화)**: `TowerPlacer.tileSize`가 `StageBuilder.TileSize`와 독립(수동 동기화). 불일치 시 풋프린트 조회 어긋남 → Awake 경고로 방어, 근본 해소는 WL-007과 함께.
- ~~WL-007 (좌표 이원화)~~ — **회피**(배치 측 변환 안 함). 단 그리드 축 정렬 가정 + `tileSize` 동기화(WL-032)에 의존.
- **WL-005 (레이어)**: 타일이 `_placementMask`(Ground) + Collider, 타워 배치물은 `Selectable`, 타워 레이어는 `_placementMask` 제외.
- **WL-001**: 타워 비용/스탯 출처(`TowerAsset`) — 자원 훅 전제.
- **WL-009**: 용어 — 본 문서 '셀/타일'은 배틀맵 기준, GDD 병사 '웨이포인트'와 무관.
- 점유 수명주기: 타일별 플래그라 맵 리셋(타일 파괴/재생성) 시 자동 초기화.

---

## 12. 확장 여지

- **가변 풋프린트**: 코드는 W×H 지원(구현됨). 현재 CSV 데이터가 전부 1×1이라 시각적으론 1×1 — designer가 CSV GridWidth/Height를 키우면 즉시 확대. (타워 프리팹 자체의 W×H 비주얼은 별도.)
- **철거/판매·재배치**: `Occupied` 해제 + 자원 환급.
- **허용 규칙 강화**: '도로 인접만' 또는 맵빌더 '건설가능' 타일 명시(Q1 대안).
- **지원 타워**(haste_tower 등, WL-026): 인접 셀 버프 — 배치 규칙엔 영향 없음.
