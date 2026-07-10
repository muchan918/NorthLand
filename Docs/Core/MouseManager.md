# MouseManager 설계 문서

게임 내 **모든 마우스 클릭/포인터 입력**을 한 곳에서 처리하는 중앙 매니저 문서.
개발 중 마우스 상호작용(오브젝트 배치, 선택, 패널 열기 등)을 구현·확장할 때 참고한다.

- 관련 이슈: **#9**
- 프로젝트는 신규 Input System(`InputSystem_Actions.inputactions`)을 사용한다.
- 구현 위치: `Assets/Personal/n0wst4ndup/MouseManager/`
- 이 문서는 **현재 구현된 구조**를 정리한 것이다. 코드를 바꾼 사람은 이 문서도 함께 갱신해 어긋나지 않게 유지한다. 미구현 항목은 [8. 미확정/TODO](#8-미확정--todo)에 모아둔다.

> ⚠️ 하이라이트 연출, 그리드 스냅, 배치 가능 셀 검사 등 일부 세부는 **아직 미구현(TODO)**이다.

## 1. 목적 · 핵심 원칙

**단일 책임**: 포인터 입력을 읽고(위치·클릭), 월드 레이캐스트를 수행하고, 클릭 결과를 라우팅하는 것은
`MouseManager` **하나만** 담당한다.

- 다른 컴포넌트는 `Mouse`/`Keyboard`를 직접 읽지 않는다.
- 클릭에 반응할 오브젝트는 **`ISelectable`을 구현**한다.
- 배치를 시작할 UI/시스템은 **`MouseManager`에 배치 요청(`PlacementRequest`)을 넘긴다.**

입력 처리 지점을 한 곳으로 모아 버그 추적과 새 상호작용 모드 추가를 쉽게 한다.

## 2. 책임 범위

| 담당함 (MouseManager) | 담당하지 않음 (다른 시스템) |
|---|---|
| 포인터 위치·좌/우클릭·Esc 감지 | 배치 가능 여부 판정 규칙 → **그리드/검증 시스템** (`CanPlaceAt`) |
| UI 위 클릭인지 월드 클릭인지 구분 | 어떤 정보 패널을 그릴지 → **UI (`TowerInfoUI`)** |
| 월드 레이캐스트·히트 오브젝트 판별 | 타워/건물/병사의 생성·능력치 → **각 배치물 시스템** |
| 현재 상호작용 모드(상태) 관리 | 자원 차감·해금 조건 → **경영/자원 시스템** |
| 선택 결과를 `OnSelectionChanged`로 통지 | |

## 3. 상태 구조 (State Machine)

입력은 매 프레임 `Mouse.current`로 직접 읽고, 상태에 따라 분기한다.

```
        BeginPlacement(request)
   ┌───────────────────────────────►┐
   │                                 │
┌──┴───────────┐               ┌─────▼────────┐
│     Idle     │               │  Placement   │
│  (선택 모드) │               │  (배치 모드) │
└──┬───────────┘               └─────┬────────┘
   │  ▲                               │
   │  └───────────────────────────────┘
   │    · 유효 셀 좌클릭 → 배치 후 복귀
   │    · 우클릭 / Esc  → 취소 후 복귀
   │
   └─ 좌클릭: 배치물 히트→선택 / 빈 곳 히트→선택 해제
```

- **Idle (기본)**: 좌클릭으로 배치된 오브젝트를 선택/해제.
- **Placement**: 고스트(프리뷰)가 마우스를 따라다니고, 유효 위치에서 좌클릭하면 배치.

## 4. 시나리오별 흐름

### 4.1 오브젝트 배치 (요구사항 ①)

1. UI 버튼(`PlacementButton`)의 OnClick → `MouseManager.BeginPlacement(request)` 호출.
   `request`(`PlacementRequest`)에는 **고스트 프리팹**, **배치 가능 판정(`CanPlaceAt`)**, **배치 확정 콜백(`OnConfirmed`)**, **연속 배치 여부(`KeepPlacingAfterConfirm`)**가 담긴다.
2. `Placement` 상태로 전환, 고스트 프리뷰 생성.
3. 매 프레임: 포인터 → 배치 표면(`_placementMask` = Ground) 레이캐스트 → (`Snap`) → 고스트 이동 → `CanPlaceAt`로 유효 여부 질의.
4. **좌클릭**: UI 위면 무시 / 유효하면 `OnConfirmed(pos)` 후 (`KeepPlacingAfterConfirm`가 false면) `Idle` 복귀.
5. **우클릭 / Esc**: 취소, 고스트 제거, `Idle` 복귀.

> 현재 `CanPlaceAt`은 항상 `true`(=Ground면 무조건 배치). 셀 점유·빌드 영역 검사는 TODO.
> 고스트 프리팹에는 **Collider를 두지 않는다** — 배치 레이캐스트가 커서 밑 고스트 자신을 맞는 것을 막기 위함.

### 4.2 배치된 오브젝트 선택 → 패널 표시 (요구사항 ②)

1. `Idle`에서 좌클릭. UI 위면 월드 레이캐스트를 건너뜀.
2. 선택 후보(`_selectableMask` = Selectable) 레이캐스트 → `TryGetComponent<ISelectable>`로 최종 확정.
   - `ISelectable` 히트 → 이전 선택 해제 후 새 오브젝트 선택(`OnSelected`) + `OnSelectionChanged` 통지.
   - 빈 곳/바닥 히트 → 선택 해제.
3. 선택된 오브젝트(`SelectableTest`)가 `OnSelected`에서 **`TowerInfoUI.Instance.ShowInfo(...)`**로 자기 정보를 표시하고, `OnDeselected`에서 `HideInfo()`로 닫는다.

> `MouseManager`는 패널을 직접 모른다. "선택됨"만 알리고, 실제 표시는 배치물이 `TowerInfoUI`(싱글톤)를 호출한다.

### 4.3 단일 책임 · 확장성 (요구사항 ③)

- 입력 읽기는 `MouseManager`에서만. 클릭 반응은 `ISelectable` 구현, 배치는 `PlacementRequest` 전달로 참여.
- 새 상호작용 모드(예: 야간 스킬 타겟팅)는 새 상태만 추가하면 되고 기존 코드 영향 최소화.

## 5. 레이캐스트 레이어 (선택 / 배치 분리)

레이캐스트 목적이 둘이라 마스크도 둘로 나눈다.

| 마스크 | 레이어 | 용도 |
|---|---|---|
| `_selectableMask` | `Selectable` | 선택 후보. 최종 선택 여부는 `ISelectable` 유무로 판정하므로 **레이어는 굵은 필터**일 뿐 |
| `_placementMask` | `Ground` | 배치 표면. 고스트가 이 위에 올라간다 |

- "선택 가능한가"는 레이어가 아니라 **`ISelectable` 컴포넌트가 결정** → 타입(타워/건물/병사)마다 레이어를 팔 필요 없음.
- `_placementMask`에서 배치물 레이어를 빼두면, 커서가 기존 건물 위에 있어도 그 **뒤 바닥**을 잡는다. (겹침 방지는 레이어가 아니라 `CanPlaceAt`에서 처리)

## 6. 확장 포인트

- **새 상호작용 모드**: 지금은 `enum Mode { Idle, Placement }`. 모드가 늘면 `IMouseState`(Enter/Update/Exit) 기반 State 패턴으로 승격.
  - 스킬 타겟팅(GDD 6.5): "스킬 선택 → 위치 지정 → 사용"도 배치와 같은 구조.
  - 병사 배치(GDD 6.4): 웨이포인트 위에서만 유효한 `PlacementRequest`로 재사용.
- **배치물 종류 확장**: `PlacementRequest`의 `CanPlaceAt`/`OnConfirmed`만 다르게 구성. 매니저 본체는 그대로.
- **선택 반응 확장**: 새 배치물은 `ISelectable`만 구현하면 선택·패널 흐름에 자동 편입.

## 7. 구현 현황 (실제 파일)

| 파일 | 역할 |
|---|---|
| `scripts/MouseManager.cs` | 중앙 매니저(싱글톤 `Instance`). 상태 관리·레이캐스트·라우팅 |
| `scripts/ISelectable.cs` | 선택 인터페이스(`OnSelected`/`OnDeselected`) |
| `scripts/PlacementRequest.cs` | 배치 요청 데이터 |
| `scripts/TowerInfoUI.cs` | 정보 패널(싱글톤 `Instance`, `ShowInfo`/`HideInfo`) |
| `scripts/Helper/SelectableTest.cs` | (테스트) 선택 시 색 변경 + 패널 표시 |
| `scripts/Helper/PlacementButton.cs` | (테스트) 버튼 클릭 → 배치 시작 |

- **레이어**: `Ground`(배치 표면), `Selectable`(선택 후보)
- **프리팹**: `Ghost`(고스트, Collider 없음), `TestTower`(배치물, Collider + `SelectableTest`)
- **씬**: `scenes/MouseEventTest.unity`
- ※ `Helper/*`, `Ghost`/`TestTower`, 테스트 씬은 **검증용**이다. 실제 게임 배치물·빌드 패널로 교체 예정.

## 8. 미확정 / TODO

- [ ] **하이라이트/연출**: 유효/무효 셀 표시, 호버 하이라이트, 선택 표시 (전부 미구현)
- [ ] **그리드 스냅**: `Snap()`이 현재 좌표를 그대로 반환 → 그리드 좌표계·셀 크기 확정 후 스냅 구현
- [ ] **배치 가능 셀 검사**: `CanPlaceAt`이 항상 `true` → 점유 여부·빌드 가능 영역 검사 연동
- [ ] **선택 대상 탐색**: 콜라이더가 자식/부모에 있을 때 `GetComponentInParent` 등 탐색 규칙
- [ ] **카메라**: 경영/전투가 카메라를 분리하면 `_camera` 단일 참조를 커서 밑 카메라 기준으로 재검토
- [ ] **데이터 연동**: `TowerInfoUI`가 문자열 대신 실제 타워/건물 데이터 객체를 받아 표시

## 9. 참고

- 신규 Input System 매뉴얼: https://docs.unity3d.com/Packages/com.unity.inputsystem@latest
- GDD 관련 시스템: `Docs/GDD.md` 6.2(타워 배치) · 6.4(병사 배치) · 6.5(스킬)
