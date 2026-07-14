# 전투 공간 타워 배치 — 기능 명세

> **상태**: 코드 구현 완료(컴파일 통과) · **씬 와이어링/플레이 검증 대기**
> **소유**: n0wst4ndup(배치 흐름·검증·어댑터) · SUNGSOO(타워 프리팹) · muchan(타워 데이터·자원)
> **구현 파일**:
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/BattleTile.cs` (신규)
> - `Assets/Scripts/GameManager/MouseManager/TowerPlacement/TowerPlacer.cs` (신규)
> - `Assets/Scripts/GameManager/MouseManager/PlacementRequest.cs` (계약 변경)
> - `Assets/Scripts/GameManager/MouseManager/MouseManager.cs` (Snap 위임·히트 전달)
> - `Assets/Scripts/GameManager/MouseManager/Helper/PlacementButton.cs` (테스트 헬퍼 시그니처)
> **관련**: GDD §5.1·§6.2, MouseManager #9, WL-001/005/009
> **참조**: `Docs/Core/MouseManager.md`, `Docs/BattleMapBuilder/BattleMapBuilder.md`, `Docs/Review/SystemMap.md`
> 코드가 이 명세와 어긋나면 문서를 갱신한다(팀 계약 #7).

---

## 0. 설계 요지 (구현 방향)

팀 합의로 **배치 측(n0wst4ndup)이 필요한 코드를 추가하고, BattleMapBuilder에 그리드 API를 신설하지 않는** 방향을 택했다. 그 결과 초안 스펙(그리드 질의·좌표 변환을 맵빌더가 소유)과 달리:

- **월드↔셀 좌표 변환이 없다.** 맵은 타일을 셀 중심에 GameObject로 스폰하므로, **배치 레이캐스트가 맞힌 타일 GameObject가 곧 셀**이다. 셀 중심 = 그 타일의 `transform.position`. → 좌표 이원화(WL-007)를 **해소가 아니라 회피**(배치 측은 변환하지 않는다).
- **타일 종류·점유는 타일에 붙인 `BattleTile` 마커**로 안다 (맵빌더 질의 API 불요).
- **BattleMapBuilder 코드 변경 없음.**

---

## 1. 목적

낮(경영) 페이즈에 플레이어가 **전투 공간 그리드의 허용된 셀**에 타워를 배치하는 상호작용. 밤에 타워는 사거리 내 적을 자동 공격하지만(GDD §5.2), 본 명세는 **배치까지**만 다룬다.

구현상 이 기능은 MouseManager의 두 미구현 지점(`Snap` 항등, `CanPlaceAt` 항상 true)을 **타일 마커 기반 스냅·검증으로 실체화**한 것이다.

---

## 2. 범위

**In**
- "허용 셀"(건설 가능) 판정 규칙 (§4)
- 고스트 스냅(타일 중심) → 유효/무효 → 확정 배치 (기존 `PlacementRequest`를 히트 인지형으로 확장, 새 상태 없음)
- 타일 종류·점유 식별 (`BattleTile` 마커)
- 셀 점유 (v1: 1타일 1타워, 타일별 bool)
- 유효/무효 시각 피드백 요구사항

**Out (훅/후속)**
- 자원 차감·낮 전용 게이팅 → §8 훅 (미구현 TODO)
- 타워 스탯·공격·투사체 → Combat
- 타워 정보 패널 연동 → WL-011
- 철거/판매, 가변 풋프린트 → §12 (v1은 1×1)
- 셀 하이라이트 아트 → 후속

---

## 3. 용어 · 좌표

| 용어 | 정의 |
| --- | --- |
| 셀(Cell) | 배치 최소 단위 = 전투 맵 타일 1개(GameObject). `BattleTile` 마커를 가진다 |
| 셀 중심 | 타일 GameObject의 `transform.position` (맵이 타일을 셀 중심에 스폰) |
| TileKind | Grass(건설가능) / Road(경로) / Lava(위험) |
| 점유 | `BattleTile.Occupied` (타일별 런타임 bool) |

**좌표 (WL-007)**: 배치 측은 월드↔셀 변환을 하지 않는다. 배치 레이캐스트 히트의 **타일 GameObject를 직접** 다루고, 셀 중심은 그 타일의 `transform.position`이다. (맵빌더는 내부적으로 battlespace 로컬 정수 그리드(MapSize=7, TileSize=5)로 타일을 배치하지만, 그 좌표계는 배치 측에 노출되지 않으며 알 필요도 없다.)

---

## 4. 허용 위치 규칙

셀이 **배치 가능** = 히트한 타일에 `BattleTile`이 있고 **`Kind == Grass` && `Occupied == false`**.

이 한 조건이 초안의 규칙 전부를 함의한다:

- **인맵**: 타일이 존재 ⇒ 생성된 블록 안. 맵 밖이면 히트에 타일/`BattleTile`이 없음 → 무효.
- **비도로·비용암**: 스폰된 타일은 grass/road/lava 중 **정확히 하나**. Grass ⇒ 도로도 용암도 아님.
- **비특수**: 스폰 지점·최종 목표는 경로(도로) 위 또는 별도 오브젝트라 Grass 타일이 아님 → 자동 제외.
- **미점유**: `!Occupied`.

> **결정(Q1)**: 별도 '건설가능' 타일 지정 없이 **grass 타일 = 건설 가능**. (도로 인접 제한·명시 지정은 §12 확장 여지.)

---

## 5. 시스템 책임 분담

| 단계 | 소유 | 비고 |
| --- | --- | --- |
| 포인터 입력·레이캐스트·고스트·상태·**히트 전달** | **MouseManager** | 그리드 규칙 무지(제네릭). 계약 #1·#6 |
| 스냅·허용 판정·타워 생성·점유 표시 | **TowerPlacer** (n0wst4ndup, 신규 어댑터) | 배치 규칙 전부 여기 |
| 타일 종류·점유 데이터 | **BattleTile** 마커 | 타일에 부착 — §6 와이어링 |
| 타워 프리팹·스탯·생성물 | **Combat** | `towerPrefab` |
| 자원 차감 | **Management** (`ResourceWallet.TrySpend`) | §8 훅 |
| 정보 패널 | **UI** (`TowerInfoUI`) | WL-011 |
| **BattleMapBuilder** | **코드 변경 없음** | 타일이 `BattleTile`을 갖게 하는 건 와이어링(§6) |

---

## 6. 타일 식별 메커니즘 (`BattleTile` 마커)

> 초안 §6의 "BattleMapBuilder 그리드 질의 API(TryWorldToCell/CellToWorld/GetTileKind/점유)"는 **불요** — 아래로 대체됐다.

- `BattleTile { TileKind Kind; bool Occupied }` — 데이터 전용 컴포넌트(판정 로직 없음).
- 어댑터는 배치 히트에서 **`hit.collider.GetComponentInParent<BattleTile>()`**로 타일을 얻는다.
- 셀 중심 = `tile.transform.position`, 점유 = `tile.Occupied`.

**타일 태깅 의존 (와이어링 — 현재 미구현, 선결 과제)**: 타워를 놓으려면 타일이 `BattleTile`(Kind 설정)을 가져야 한다. 소스 타일 프리팹이 **`Assets/Imported/`(벤더링, 편집 금지)**에 있으므로, **Imported 프리팹을 직접 편집하지 않고** 다음 중 하나로 태깅한다:

- **(a) 로컬 프리팹 복사본** — grass/road/lava 3종을 프로젝트 폴더로 복사해 `BattleTile`+Kind 부착 후 `StageBuilder`의 grass/road/lava 필드에 재지정. (권장 · 인스펙터만, 코드 무수정)
- **(b) 스폰 시 태깅** — `StageMapSpawner`에서 타일 생성 직후 `AddComponent<BattleTile>()`+Kind (~4줄, 공유 맵빌더 소량 수정).
- **(c) 런타임 자가 태깅** — 배치 측 컴포넌트가 생성 후 이름으로 분류해 부착(생성 이후 타이밍 훅 필요).

(실제 타일 프리팹: grass=`TB_Env_GroundA`, road=`TB_Env_GroundDC_plain`, lava=`TB_Env_River_Water` — `Assets/Imported/muchan/TARBO-TowerDefensePack/`.)

---

## 7. 배치 흐름 (구현)

**`PlacementRequest`가 히트 인지형으로 확장됨** — MouseManager는 오히려 더 제네릭해졌다(그리드/스냅 규칙을 요청이 소유):

- `Func<RaycastHit, Vector3> Snap` — 히트 → 배치 기준 위치. null이면 `hit.point`.
- `Func<RaycastHit, bool> CanPlaceAt`
- `Action<RaycastHit, Vector3> OnConfirmed` — (히트, 스냅 위치)

**흐름** (새 상태 없음 — Idle/Placement 2상태):
- 진입: 버튼 OnClick → `TowerPlacer.BeginTowerPlacement()` → request 구성 → `MouseManager.BeginPlacement`.
- 매 프레임(Placement): `_placementMask` 레이캐스트 → `Snap(hit)`(타일 중심) → 고스트 이동 → `CanPlaceAt(hit)`.
- 확정(좌클릭·유효): `OnConfirmed(hit, pos)` → 타일 중심에 타워 생성 + `tile.Occupied=true` → (`KeepPlacing=false`면) Idle 복귀.
- 취소: 우클릭/Esc.

**TowerPlacer 판정**:
- `Snap`: 타일 중심 수평 스냅(높이는 히트 표면 유지).
- `CanPlaceAt`: `BattleTile.Kind==Grass && !Occupied`. [+ §8 훅]
- `OnConfirmed`: 상태 재확인 후 `towerPrefab` 생성, 점유 표시. [+ §8 훅]

**전제(와이어링)**: 타일이 `_placementMask`(Ground) 레이어 + Collider 보유(레이캐스트가 타일을 맞혀야 함), `TowerPlacer`에 `towerPrefab`/`ghostPrefab` 지정(고스트는 Collider 없음), 씬에 `MouseManager` 존재.

---

## 8. 통합 훅 (자원 · 페이즈) — 미구현 TODO

> **결정(Q3)**: 배치·검증 흐름만 구현하고, 아래는 `TowerPlacer`에 **훅 주석만** 있다.

- **자원 차감**: `OnConfirmed` 최초에 `ResourceWallet.TrySpend(kind, cost)` 성공 시에만 생성·점유. 비용 출처 = `TowerAsset`(muchan) — Combat 미소비(WL-001) → 비용 소스 확정 TODO.
- **낮 전용**(계약 #5): 진입 시 `DayNightManager.CurrentPhase` 확인(`Instance` null 가능 → 체크). TODO.

---

## 9. 시각 피드백 — 미구현 TODO

- 고스트 유효/무효 색: `MouseManager.UpdatePlacement`에 TODO 주석만 있고 아직 색이 바뀌지 않는다.
- 셀 하이라이트(그리드 오버레이): 후속(MouseManager.md §8).

---

## 10. 인수 조건 (Acceptance Criteria)

> 전제: 타일에 `BattleTile`이 부착됨(§6 와이어링 완료).

- [ ] 고스트가 타일 중심으로 스냅된다.
- [ ] road·lava·타일없음·점유 타일 위에서 무효, 좌클릭 무반응.
- [ ] grass·미점유 타일에서 유효, 좌클릭 시 타워가 **타일 중심**에 생성되고 점유된다.
- [ ] 같은 타일에 두 번째 타워 배치 불가.
- [ ] 우클릭/Esc로 취소.
- [ ] (훅) 자원 부족 시 확정 실패 / 밤 진입 차단 — 훅 연동 후.

검증: 씬 와이어링 후 개인 테스트 씬 Play 확인(팀 컨벤션 — 유닛 테스트 없음). 시각 검증은 씬 뷰 스크린샷(`Docs/Tools/unity-cli-guide.md` §4.J).

---

## 11. TODO / 의존

- **타일 `BattleTile` 태깅**(§6) — **와이어링 대기(핵심 선결)**. Imported 직접편집 금지 → 로컬 복사본/스폰태깅/런타임태깅 중 택1.
- ~~WL-004 (BattleMapBuilder 그리드 질의 API)~~ — **불요**(타일 마커로 대체).
- ~~WL-007 (좌표 이원화)~~ — **회피**(배치 측이 변환하지 않음, 타일 트랜스폼 사용).
- **WL-005 (레이어)**: 타일이 `_placementMask`(Ground) 레이어 + Collider를 가져야 함. 타워 배치물은 `Selectable`, 타워 자체 레이어는 `_placementMask`에서 제외.
- **WL-001**: 타워 비용 출처(`TowerAsset` 마이그레이션) — 자원 훅(§8) 전제.
- **WL-009**: 용어 — 본 문서 '셀/타일'은 배틀맵 기준, GDD 병사 '웨이포인트'와 무관.
- 점유 수명주기: 타일별 플래그라 맵 리셋(타일 파괴/재생성) 시 자동 초기화.
- **이름 기반 대안**: 태깅을 원치 않으면 `TowerPlacer`를 인스턴스 이름 분류 + 자체 점유 관리로 전환 가능(프리팹 교체 시 취약, 현재 미채택).

---

## 12. 확장 여지

- **가변 풋프린트**(2×2 등): 타일별 플래그 → 셀 집합 점유로 일반화. 타워 데이터에 크기 필드 필요.
- **철거/판매·재배치**: `Occupied` 해제 + 자원 환급.
- **허용 규칙 강화**: '도로 인접만' 또는 맵빌더 '건설가능' 타일 명시(Q1 대안).
- **지원 타워**(haste_tower 등, WL-026): 인접 셀 버프 — 배치 규칙엔 영향 없음.
