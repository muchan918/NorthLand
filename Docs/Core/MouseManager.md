# MouseManager 설계 문서

게임 내 **모든 마우스 클릭/포인터 입력**을 한 곳에서 처리하는 중앙 매니저 문서.
개발 중 마우스 상호작용(오브젝트 배치, 선택, 패널 열기 등)을 구현·확장할 때 참고한다.

- 관련 이슈: **#9**
- 프로젝트는 신규 Input System(`InputSystem_Actions.inputactions`)을 사용한다.
- 구현 위치: `Assets/Scripts/GameManager/MouseManager/`
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
| 선택 결과를 `OnSelectionChanged`로 통지 | 툴팁 내용·연출·색 → **툴팁 UI + 각 `IHoverable` 공급자** |
| 커서 밑 호버 대상(`IHoverable`)을 `OnHoverChanged`로 통지 | 호버 하이라이트 연출(색 변경 등) → **미구현(TODO)** |
| 포인터 화면 좌표를 `PointerPosition`으로 노출 | |

## 3. 상태 구조 (State Machine)

입력은 매 프레임 `Mouse.current`로 직접 읽고, 상태에 따라 분기한다.

```
        BeginPlacement(request)
   ┌───────────────────────────────►┐
   │                                 │
┌──┴───────────┐               ┌─────▼────────┐
│     Idle     │               │  Placement   │
│  (선택 모드) │◄──────────────┤  (배치 모드) │
└──┬───────────┘               └──────────────┘
   │  ▲                               │
   │  └───────────────────────────────┘
   │    · 유효 셀 좌클릭 → 배치 후 복귀
   │    · 우클릭 / Esc  → 취소 후 복귀
   │
   │         BeginSkillTargeting(request)
   │  ┌───────────────────────────────►┐
   │  │                                 │
   └──┴───────────┐               ┌─────▼──────────┐
                   │               │ SkillTargeting │
                   │◄──────────────┤ (스킬 조준 모드)│
                   └───────────────└────────────────┘
      · 도로 타일 좌클릭 → OnConfirmed(pos) 후 복귀
      · 우클릭 / Esc → 취소 후 복귀
```

- **Idle (기본)**: 좌클릭으로 배치된 오브젝트를 선택/해제. **매 프레임 커서 밑 `IHoverable`을 추적**해 대상이 바뀔 때 `OnHoverChanged`로 통지(툴팁용).
- **Placement**: 고스트(프리뷰)가 마우스를 따라다니고, 유효 위치에서 좌클릭하면 배치. (배치 중에는 호버 통지를 끈다 — `BeginPlacement`이 호버를 클리어)
- **SkillTargeting** (#103): 스킬 범위 인디케이터(고스트)가 마우스를 따라다니되 **전투 타일 위에서만 표시**(그 외엔 숨김)하고, **도로 타일 위에서만**(초록) 좌클릭으로 시전을 확정한다(그 외 전투 타일은 빨강 = 시전 불가). 인디케이터는 히트 표면에 얹혀 낮은 도로 채널 안에 앉는다. `Placement`와 구조는 비슷하지만 그리드 스냅·점유 검증이 없다 — §4.5 참고.

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

### 4.4 호버 툴팁 (#38)

1. `Idle`에서 매 프레임 커서 밑을 `_selectableMask`로 레이캐스트 → `TryGetComponent<IHoverable>`.
2. 호버 대상이 **바뀔 때만** `OnHoverChanged(IHoverable)` 통지(같으면 무시, 없으면 `null`). UI 위/배치 모드에서는 대상을 `null`로 본다.
3. `TooltipUI`(임시 싱글톤, `Assets/Scripts/GameManager/MouseHover`)가 `OnHoverChanged`를 구독 →
   대상이 있으면 `IHoverable.GetTooltipContent()`로 내용을 **pull**해 표시, 없으면 숨김.
   `GetTooltipContent()`는 호버 시점마다 호출되므로 동적 값(버프 레벨·현재 생산량 등)도 그 순간 계산해 넘길 수 있다.
4. 툴팁은 `MouseManager.PointerPosition`(→ `Mouse.current` 직접 폴링 금지, 계약 #1)을 따라 이동하며 화면 밖으로 나가지 않게 clamp한다.

> `MouseManager`는 "무엇이 호버됐다"만 통지한다. 헤더/본문 문자열·색·포맷은 전적으로 각 `IHoverable` 공급자가 정한다
> (건물=`BuildingTooltipSource`가 `이름 - 역할`+설명+타입별 색, 그래프형 버프 건물 등도 같은 인터페이스로 재사용).
> 이 계보는 `TowerInfoUI`/`BuildingInfoUI`와 마찬가지로 **UIManager 도입 시 흡수**될 임시 싱글톤이다.

### 4.5 스킬 타겟팅 (#103, GDD §5.5)

1. 스킬 버튼(`SkillButtonView`)의 OnClick → `SkillManager.CanCast()`(밤 여부+쿨다운) 확인 후
   `MouseManager.BeginSkillTargeting(request)` 호출. `request`(`SkillTargetRequest`)에는 **범위 인디케이터 프리팹**,
   **확정 콜백(`OnConfirmed(Vector3)`)**만 담긴다 — `PlacementRequest`와 달리 `Snap`/`CanPlaceAt` 없음(그리드 개념 불필요).
2. `SkillTargeting` 상태로 전환, 인디케이터(`SkillRangeIndicator`) 생성.
3. 매 프레임: 포인터 → `_placementMask`(Ground) 레이캐스트 → 히트한 타일의 `CombatMapTileView`를 읽어 판정한다.
   - **전투 타일이 아니면**(빈 칸·타일 사이 틈·맵 밖) 인디케이터를 **숨긴다**(`SetActive(false)`).
   - 전투 타일이면 표시하고 **히트 표면(`hit.point`)에 배치** — 도로 메시가 낮게 모델링돼 있어 도로 위에선 낮은 채널 안에 앉는다.
   - `TileType == Road`면 `SkillRangeIndicator.SetValid(true)`(초록=시전 가능), 그 외 전투 타일(잔디/물)은 `SetValid(false)`(빨강=시전 불가).
4. **좌클릭**: UI 위면 무시 / **도로 타일 위에서만** `OnConfirmed(hit.point)` 호출(보통 `SkillManager.CastAt(pos)` 연결) 후 `Idle` 복귀. 도로 밖 좌클릭은 무시(조준 모드 유지).
5. **우클릭 / Esc**: 취소, 인디케이터 제거, `Idle` 복귀.

> `Placement`와 상태 흐름은 같지만, 배치 결과가 영구 오브젝트가 아니라 즉발 효과라
> `KeepPlacingAfterConfirm`(연속 배치) 개념이 없다.
> 도로 전용 게이팅·유효/무효 색은 **MouseManager가 소유**한다(`CombatMapTileView.TileType` 질의).
> `SkillTargetRequest`엔 여전히 `CanPlaceAt` 류 훅이 없다 — 스킬 규칙이 단순(도로 여부)해 매니저에 인라인.
> 규칙이 복잡해지면 `PlacementRequest`처럼 요청 측으로 옮긴다.

## 5. 레이캐스트 레이어 (선택 / 배치 분리)

레이캐스트 목적이 둘이라 마스크도 둘로 나눈다.

| 마스크 | 레이어 | 용도 |
|---|---|---|
| `_selectableMask` | `Selectable` | 선택 후보. 최종 선택 여부는 `ISelectable` 유무로 판정하므로 **레이어는 굵은 필터**일 뿐. **호버 감지도 이 마스크를 재사용**(최종 판정은 `IHoverable` 유무) |
| `_placementMask` | `Ground` | 배치 표면. 고스트가 이 위에 올라간다 |

- "선택 가능한가"는 레이어가 아니라 **`ISelectable` 컴포넌트가 결정** → 타입(타워/건물/병사)마다 레이어를 팔 필요 없음.
- `_placementMask`에서 배치물 레이어를 빼두면, 커서가 기존 건물 위에 있어도 그 **뒤 바닥**을 잡는다. (겹침 방지는 레이어가 아니라 `CanPlaceAt`에서 처리)
- **스킬 타겟팅**은 `_placementMask` 히트에서 `CombatMapTileView`(전투 타일 데이터)를 읽어 도로 여부를 판정한다 — 레이어가 아니라 컴포넌트가 최종 판정(선택/호버와 동일 원칙).

## 6. 확장 포인트

- **새 상호작용 모드**: 지금은 `enum Mode { Idle, Placement, SkillTargeting }`(#103에서 `SkillTargeting` 추가).
  모드가 더 늘면 `IMouseState`(Enter/Update/Exit) 기반 State 패턴으로 승격 검토.
  - 스킬 타겟팅(GDD §5.5): **구현 완료(#103)** — `BeginSkillTargeting(SkillTargetRequest)`, §4.5 참고.
  - 병사 배치(GDD §5.4): 웨이포인트 위에서만 유효한 `PlacementRequest`로 재사용 예정(미착수).
- **배치물 종류 확장**: `PlacementRequest`의 `CanPlaceAt`/`OnConfirmed`만 다르게 구성. 매니저 본체는 그대로.
- **선택 반응 확장**: 새 배치물은 `ISelectable`만 구현하면 선택·패널 흐름에 자동 편입.

## 7. 구현 현황 (실제 파일)

| 파일 | 역할 |
|---|---|
| `MouseManager.cs` | 중앙 매니저(싱글톤 `Instance`). 상태 관리·레이캐스트·라우팅 |
| `ISelectable.cs` | 선택 인터페이스(`OnSelected`/`OnDeselected`) |
| `IHoverable.cs` | 호버 인터페이스(`GetTooltipContent()`/`OnHoverEnter()`/`OnHoverExit()`). 호버 시 툴팁 내용을 pull 공급(내용 없으면 `null` 반환 가능) + 호버 진입/이탈 훅(하이라이트 등 연출용, #67) |
| `PlacementRequest.cs` | 배치 요청 데이터 |
| `SkillTargetRequest.cs` | 스킬 타겟팅 요청 데이터(#103) — `GhostPrefab`/`OnConfirmed(Vector3)`/`OnEnded`만 있는 `PlacementRequest`의 경량 버전 |
| `TowerInfoUI.cs` | 정보 패널(싱글톤 `Instance`, `ShowInfo`/`HideInfo`) |
| `Helper/SelectableTest.cs` | (테스트) 선택 시 색 변경 + 패널 표시 |
| `Helper/PlacementButton.cs` | (테스트) 버튼 클릭 → 배치 시작 |

호버 툴팁 UI/어댑터는 `Assets/Scripts/GameManager/MouseHover`에 있다:

| 파일 | 역할 |
|---|---|
| `TooltipContent.cs` | 툴팁 표시 데이터(헤더/본문/색). 구체 개념에 무지한 순수 struct |
| `TooltipUI.cs` | 커서 추적 범용 툴팁 뷰(임시 싱글톤 `Instance`, `Show`/`Hide`). `OnHoverChanged` 구독 |
| `BuildingTooltipSource.cs` | 건물용 `IHoverable` 어댑터. `BuildingAsset`/`BuildingData`를 읽어 `이름 - 역할`+설명 구성(muchan 코드는 읽기만) |
| `BuildingTooltipPalette.cs` + `BuildingTooltipPalette.asset` | `BuildingType`→(헤더색, 배경색) 팔레트 SO |
| `Assets/Personal/n0wst4ndup/MouseHover/Scenes/MouseHover.unity` | (테스트) 건물 5종(Production·General·Skill 3타입 모두 커버) + 툴팁 검증 씬 |

- **레이어**: `Ground`(배치 표면), `Selectable`(선택 후보)
- **프리팹**: `Ghost`(고스트, Collider 없음), `TestTower`(배치물, Collider + `SelectableTest`)
- **씬**: `Assets/Personal/n0wst4ndup/MouseManager/scenes/MouseEventTest.unity`
- ※ `Helper/*`, `Ghost`/`TestTower`, 테스트 씬은 **검증용**이다. 실제 게임 배치물·빌드 패널로 교체 예정.

## 8. 미확정 / TODO

- [x] **호버 하이라이트**: **구현됨(#67)** — `IHoverable`에 `OnHoverEnter()`/`OnHoverExit()` 추가,
      `MouseManager.SetHover`가 대상 전환 시 호출(`_hovered?.OnHoverExit()` → 재할당 →
      `_hovered?.OnHoverEnter()`). 첫 구현체: `TerritoryNodeView`(영토 확장 가능 노드 호버 시 색
      변경, 벗어나면 원래 색 복귀 — `Assets/Scripts/ManagementSpace/Territory/View`). 건물
      쪽(`BuildingTooltipSource`)은 훅만 만족(빈 구현), 실제 하이라이트 연출은 후속.
- [~] **유효/무효 표시, 선택 표시**: **스킬 인디케이터는 구현됨**(#103 후속) — 도로=초록/그 외 전투 타일=빨강,
      전투 타일 밖 숨김(`SkillRangeIndicator.SetValid`, §4.5). 배치(Placement) 고스트의 유효/무효 표시와
      선택된 오브젝트 표시는 여전히 미구현
- [ ] **그리드 스냅**: `Snap()`이 현재 좌표를 그대로 반환 → 그리드 좌표계·셀 크기 확정 후 스냅 구현
- [ ] **배치 가능 셀 검사**: `CanPlaceAt`이 항상 `true` → 점유 여부·빌드 가능 영역 검사 연동
- [ ] **선택 대상 탐색**: 콜라이더가 자식/부모에 있을 때 `GetComponentInParent` 등 탐색 규칙
- [ ] **카메라**: 경영/전투가 카메라를 분리하면 `_camera` 단일 참조를 커서 밑 카메라 기준으로 재검토
- [ ] **데이터 연동**: `TowerInfoUI`가 문자열 대신 실제 타워/건물 데이터 객체를 받아 표시

## 9. 참고

- 신규 Input System 매뉴얼: https://docs.unity3d.com/Packages/com.unity.inputsystem@latest
- GDD 관련 시스템: `Docs/GDD.md` §5.2(타워 배치) · §5.4(병사 배치) · §5.5(스킬, #103에서 구현 완료)
