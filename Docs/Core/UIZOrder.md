# UI 표시 레이어(z-order) 규칙

> 관련 이슈: #188, #221  
> 정본 씬: `Assets/Scenes/GameScene.unity`  
> 상태: 씬 구조 및 설정 모달 규칙 반영 완료, 일부 모달 및 플레이 모드 검증 필요

## 1. 목적

HUD와 모달 UI의 표시 순서를 Canvas 계층의 우연한 배치에 맡기지 않고 팀 공통 규칙으로 고정한다.

상위 모달이 열린 동안에는 미니맵을 포함한 하위 UI가 포인터 입력을 받지 않아야 한다. 게임 진행을 멈춰야 하는 모달은 전용 일시정지 사유를 사용한다.

이 문서는 `GameScene` UI를 추가하거나 이동할 때 따라야 하는 표시 순서, 입력 차단 및 일시정지 규칙의 단일 기준이다.

## 2. uGUI 적용 원칙

- 서로 다른 루트 Canvas의 우선순위는 `Canvas.sortingOrder`로 결정한다.
- 같은 Canvas 안에서는 sibling 인덱스를 사용한다. Hierarchy에서 뒤 sibling일수록 위에 그려진다.
- 자식 Canvas가 부모와 독립적인 순서를 가져야 할 때만 `overrideSorting`을 켠다.
- 독립 순서가 필요하지 않은 자식 Canvas는 `overrideSorting = false`, `sortingOrder = 0`을 유지한다.
- `GraphicRaycaster`만으로는 빈 화면 영역의 입력을 차단할 수 없다.
- 모달에는 화면 전체를 덮고 `raycastTarget`이 활성화된 `Graphic`이 필요하다.
- 게임 진행을 멈춰야 하는 모달은 `Time.timeScale`을 직접 변경하지 않고 `GameSpeedController`의 전용 `GamePauseReason`을 사용한다.

모달은 다음 세 조건을 기준으로 구성한다.

1. 표시 우선순위 보장
2. 하위 UI 입력 차단
3. 필요한 경우 게임 진행 일시정지

## 3. 표준 Canvas 레이어

아래 표는 낮은 레이어에서 높은 레이어 순서다.

| 우선순위 | Canvas/오버레이 | `sortingOrder` | 용도 |
|---:|---|---:|---|
| 월드 오버레이 | `SelectionBoxView` | `50` | 드래그 선택 사각형. 입력을 받지 않으며 HUD **아래**에 그린다 |
| 기본 | `UICanvas` | `100` | 일반 HUD, 미니맵, 관리·타워·스킬·정보 패널 |
| 상위 모달 | `RewardCanvas` | `500` | 보상 선택 화면 |
| 설정 모달 | `SettingCanvas` | `700` | 인게임 설정 화면. 일반 HUD와 보상 화면보다 위, 결과 화면보다 아래 |
| 최상위 모달 | `ResultCanvas` | `900` | 게임오버·승리 결과 화면 |
| 코드 생성 오버레이 | `TowerTooltipView` | `100` (`UILayer.Hud`) | 입력을 받지 않는 툴팁. HUD 캔버스를 찾아 자식으로 붙고, 없을 때만 같은 값으로 자체 생성 |

새 루트 Canvas를 추가할 때는 임의의 큰 숫자를 사용하지 않는다. 위 범주 중 하나에 포함시키고, 새 범주가 꼭 필요하면 이 표를 먼저 갱신한다.

`SettingCanvas`와 `ResultCanvas`는 서로 다른 `sortingOrder`를 사용한다. 두 루트 Canvas가 같은 값을 사용하면 씬 계층이나 Canvas 등록 순서에 따라 그리기 순서가 달라질 수 있기 때문이다.

게임오버 또는 승리 결과 화면과 설정 화면이 동시에 활성화되더라도 `ResultCanvas`가 항상 `SettingCanvas`보다 위에 표시되어야 한다.

`TowerTooltipView`는 런타임 생성 읽기 전용 오버레이다. **`short.MaxValue`를 쓰지 않는다** — HUD 캔버스(`sortingOrder == UILayer.Hud`)를 찾아 그 자식(마지막 sibling)으로 붙고, 찾지 못한 씬에서만 같은 `UILayer.Hud` 값으로 자체 Canvas를 만든다. 결과·설정 화면 위로 뜨지 않는다(WL-107).

`SelectionBoxView`(#261)도 런타임에 전용 Canvas를 생성하는 읽기 전용 오버레이다. 두 가지 이유로 `UICanvas`에 얹지 않는다.

- **성능**: Canvas는 자식 Graphic이 하나라도 바뀌면 그 Canvas 전체의 메시를 다시 만든다. 사각형은 드래그하는 동안 매 프레임 움직이므로, `UICanvas`에 두면 HUD 전체가 매 프레임 리빌드된다. 전용 Canvas로 분리해 리빌드 범위를 사각형 자신으로 가둔다.
- **씬 병합**: 씬에 배치하지 않으므로 `GameScene` diff가 생기지 않는다.

사각형은 월드 위에 그리되 HUD보다는 아래여야 하므로 `sortingOrder = 50`을 쓴다. `GraphicRaycaster`를 붙이지 않고 모든 `Graphic`의 `raycastTarget`을 끈다 — 켜져 있으면 커서가 항상 UI 위로 잡혀 게임의 모든 클릭이 죽는다.

## 4. `UICanvas` 내부 순서

`UICanvas` 내부 UI는 Hierarchy의 sibling 순서를 사용한다. 현재 정본 씬의 아래→위 순서는 다음과 같다.

1. `ManagementCanvas`
2. `TooltipUI`
3. `TowerPanel`
4. `SkillPanel`
5. `BaseHealthBar`
6. `TowerInfoPanel`
7. `BalanceTestPanel`
8. `Minimap`
9. `EndDayConfirmPopup` — `UICanvas` 내부 모달

`PreviewHoverButton`과 `NextWavePreviewPanel`은 낮 전용 웨이브 편성 UI다. 두 오브젝트를
`UICanvas` 직속 sibling으로 두지 않고 `PhasePanelSwitcher._dayPanel`인 `TowerPanel`의 자식으로 둔다.
따라서 밤 전환 시 버튼·열린 패널·포인터 입력이 `TowerPanel`과 함께 내려가며, 별도 전역 UI 순서를
차지하지 않는다. `PreviewHoverButton`이 먼저, `NextWavePreviewPanel`이 뒤 sibling이어서 미리보기 패널이
버튼보다 위에 그려져야 한다.

`Minimap`은 `UICanvas`의 자식 Canvas이며 `overrideSorting = false`로 설정한다. 따라서 루트 Canvas처럼 별도 전역 레이어를 차지하지 않고 `UICanvas` 내부의 sibling 순서를 따른다.

독립 정렬을 사용하지 않는 자식 Canvas의 `sortingOrder`는 혼동을 방지하기 위해 `0`으로 유지한다.

`BalanceTestPanel`은 개발·밸런스 테스트용 UI이므로 일반 HUD보다 위에 둔다. 출시용 UI를 추가할 때 이 패널을 일반 표시 순서의 기준으로 사용하지 않는다.

### `UICanvas` 내부 모달

루트 Canvas를 사용하지 않는 모달은 `UICanvas`의 마지막 sibling에 배치하여 일반 HUD보다 위에 표시한다.

새 HUD를 추가할 때 내부 모달보다 뒤에 배치하지 않는다. 새로운 내부 모달이 추가되면 모달 간 우선순위를 이 문서에 명시한다.

`UICanvas` 내부 모달은 루트 Canvas의 `sortingOrder`를 사용하지 않으므로 다음 조건을 모두 만족해야 한다.

- `UICanvas`의 마지막 sibling에 배치한다.
- 화면 전체를 덮는 입력 차단 Graphic을 포함한다.
- 입력 차단 Graphic은 모달 콘텐츠보다 먼저 그려진다.
- 모달이 닫히면 입력 차단 Graphic도 함께 비활성화된다.
- 게임 진행을 멈춰야 한다면 전용 `GamePauseReason`을 사용한다.

## 5. 모달 입력 차단 및 일시정지

다음 패널은 활성화될 때 화면 전체 크기의 `Graphic`이 입력을 받아 하위 UI로의 포인터 입력 통과를 막아야 한다.

| 모달 | 정렬 방식 | 입력 차단 Graphic | 일시정지 |
|---|---|---|---|
| 보상 선택 | `RewardCanvas`, Order `500` | `RewardPanel`의 전체 화면 `Image` | `GamePauseReason.Reward` |
| 설정 | `SettingCanvas`, Order `700` | `GuardPanel`의 전체 화면 투명 `Image` | `GamePauseReason.Settings` |
| 게임오버 | `ResultCanvas`, Order `900` | `GameOverPanel`의 전체 화면 `Image` | 정책 확정 필요 |
| 승리 | `ResultCanvas`, Order `900` | `VictoryPanel`의 전체 화면 `Image` | 정책 확정 필요 |
| 낮 종료 확인 | `UICanvas`의 마지막 sibling | `EndDayConfirmPopup`의 전체 화면 Blocker Image | 필요 여부 확정 및 검증 필요 |

차단용 Graphic은 투명해도 된다. 단, 다음 조건을 모두 만족해야 한다.

- 앵커가 화면 전체를 덮는다.
- 모달 콘텐츠보다 먼저 그려지도록 모달 루트의 첫 sibling에 둔다.
- `raycastTarget`이 켜져 있다.
- 모달이 닫히면 함께 비활성화된다.

모달 아래에 있는 버튼을 개별적으로 비활성화하는 방식은 사용하지 않는다. 새 하위 UI가 추가될 때 누락될 수 있기 때문이다.

일시정지가 필요한 모달은 다음 원칙을 따른다.

- `Time.timeScale`을 모달 코드에서 직접 변경하지 않는다.
- `GameSpeedController.SetPaused()`를 사용한다.
- 모달마다 구분되는 `GamePauseReason`을 사용한다.
- 모달을 닫을 때 자신이 등록한 일시정지 사유만 해제한다.
- 다른 모달의 일시정지 상태를 함께 해제하지 않는다.

### 설정 모달

설정 화면은 `SettingCanvas` 루트 Canvas를 사용하며 다음 규칙을 따른다.

- `SettingCanvas`의 `sortingOrder`는 `700`으로 고정한다.
- 일반 HUD와 `RewardCanvas`보다 위에 표시한다.
- 최상위 모달인 `ResultCanvas`보다 아래에 표시한다.
- 설정 화면이 열리면 `GuardPanel`을 함께 활성화한다.
- `GuardPanel`은 전체 화면 Stretch Anchor를 사용한다.
- `GuardPanel`은 화면을 시각적으로 가리지 않도록 투명하게 유지한다.
- `GuardPanel`의 `raycastTarget`은 활성화하여 하위 UI 입력을 차단한다.
- 설정 패널은 `GuardPanel`보다 뒤 sibling에 배치하여 입력을 받을 수 있도록 한다.
- 설정 화면이 닫히면 `GuardPanel`도 함께 비활성화한다.
- 설정 화면을 열 때 `GamePauseReason.Settings`로 일시정지를 요청한다.
- 설정 화면을 닫을 때 `GamePauseReason.Settings`에 해당하는 요청만 해제한다.
- 설정 화면에서 `Time.timeScale`을 직접 변경하지 않는다.
- 다른 시스템이 등록한 일시정지 사유는 설정 화면을 닫더라도 해제하지 않는다.

ESC 입력 소유권은 씬별로 하나만 둔다.

- `GameScene`: `SettingUI`가 ESC를 읽어 설정 화면을 토글한다. 설정 화면을 열 때는 `MouseManager.CancelInteractions()`로 진행 중인 배치·조준·선택을 먼저 취소한다.
- `TitleScene`: `MainMenuUI`가 ESC를 읽어 세이브 패널을 토글한다. `SettingUI`는 `GameManager.Instance`가 없는 씬에서 ESC를 처리하지 않으므로 설정 화면은 UI 버튼으로만 열고 닫는다.
- 같은 씬에 ESC 소비처를 추가하지 않는다. 다른 모달 닫기나 상호작용 취소 등 두 번째 소비처가 필요해지면 직접 폴링을 늘리지 않고 중앙 Cancel 라우터와 우선순위를 먼저 확정한다.

## 6. 변경 절차

1. 씬 작업은 `Docs/Core/SceneWorkflow.md`를 따른다.
2. 최종 결과는 정본 `Assets/Scenes/GameScene.unity`에 반영한다.
3. 새 UI가 일반 HUD인지 모달인지 결정한다.
4. 모달이라면 루트 Canvas 모달인지 `UICanvas` 내부 모달인지 결정한다.
5. 루트 Canvas가 필요하면 §3의 표준 `sortingOrder`를 적용한다.
6. 일반 HUD라면 `UICanvas` 안에서 §4의 상대 우선순위에 맞게 sibling 위치를 정한다.
7. `UICanvas` 내부 모달이라면 항상 일반 HUD보다 뒤에 배치한다.
8. 모든 모달에 §5의 전체 화면 입력 차단 Graphic을 배치한다.
9. 게임 진행을 멈춰야 하는 모달에는 전용 `GamePauseReason`을 적용한다.
10. 새 루트 Canvas 또는 새로운 모달 범주를 추가하면 이 문서의 §3과 §5를 함께 갱신한다.
11. 아래 대표 겹침 및 입력 테스트를 수행한다.

## 7. 검증 체크리스트

### Canvas 정렬

- [x] `UICanvas`의 `sortingOrder`가 `100`이다.
- [x] `RewardCanvas`의 `sortingOrder`가 `500`이다.
- [x] `SettingCanvas`의 `sortingOrder`가 `700`이다.
- [x] `ResultCanvas`의 `sortingOrder`가 `900`이다.
- [x] `SettingCanvas`와 `ResultCanvas`가 동일한 `sortingOrder`를 사용하지 않는다.
- [x] `ResultUI`가 전체 화면 Stretch, Position `(0,0)`, Size Delta `(0,0)`, Scale `(1,1,1)`이다.
- [x] `Minimap`이 `UICanvas` 아래에 있고 독립 전역 정렬을 사용하지 않는다.
- [ ] `Minimap` 자식 Canvas의 `sortingOrder`가 `0`인지 확인한다.
- [x] `TowerTooltipView`가 코드 생성 오버레이로 문서에 명시되어 있다.

### 입력 차단

- [x] 보상 패널에 전체 화면 raycast 차단 Graphic이 있다.
- [x] 설정 패널에 전체 화면 `GuardPanel`이 있다.
- [x] `GuardPanel`이 전체 화면 Stretch Anchor를 사용한다.
- [x] `GuardPanel`이 투명하고 `raycastTarget = true`로 설정되어 있다.
- [x] 게임오버 패널에 전체 화면 raycast 차단 Graphic이 있다.
- [x] 승리 패널에 전체 화면 raycast 차단 Graphic이 있다.
- [ ] `EndDayConfirmPopup` 프리팹이 저장소에 정상적으로 포함되어 있다.
- [ ] `EndDayConfirmPopup`에 전체 화면 입력 차단 Graphic이 있다.
- [ ] `EndDayConfirmPopup`의 차단 Graphic에 `raycastTarget = true`가 설정되어 있다.

### 일시정지

- [x] 설정 화면이 `Time.timeScale`을 직접 변경하지 않는다.
- [x] 설정 화면이 `GamePauseReason.Settings`를 사용한다.
- [x] 설정 화면을 닫을 때 `GamePauseReason.Settings`만 해제한다.
- [ ] 설정 화면과 다른 일시정지 사유가 중첩된 상태에서 한쪽 사유만 정상적으로 해제되는지 확인한다.

### Hierarchy

- [ ] `EndDayConfirmPopup`이 `UICanvas`의 마지막 sibling에 있는지 확인한다.
- [ ] 새 일반 HUD가 `EndDayConfirmPopup`보다 뒤에 배치되지 않았는지 확인한다.
- [x] 설정 패널이 `GuardPanel`보다 뒤 sibling에 배치되어 있다.

### 플레이 모드

- [x] 보상 패널이 미니맵과 일반 HUD보다 위에 표시되는지 확인한다.
- [x] 보상 패널이 열린 동안 미니맵 클릭 이동이 발생하지 않는지 확인한다.
- [ ] 설정 화면이 일반 HUD와 보상 화면보다 위에 표시되는지 확인한다.
- [ ] 설정 화면이 열린 동안 미니맵과 하위 HUD가 입력을 받지 않는지 확인한다.
- [ ] 설정 화면을 열고 닫을 때 `GamePauseReason.Settings`가 정상적으로 등록 및 해제되는지 확인한다.
- [ ] `GameScene`에서 ESC로 설정 화면이 열리고 다시 ESC를 누르면 닫히는지 확인한다.
- [ ] `TitleScene`에서 ESC가 세이브 패널만 토글하고 `SettingUI`는 반응하지 않는지 확인한다.
- [ ] 설정 화면과 결과 화면이 동시에 활성화됐을 때 `ResultCanvas`가 위에 표시되는지 확인한다.
- [x] 게임오버·승리 화면이 열린 동안 아래 HUD 버튼이 입력을 받지 않는지 확인한다.
- [ ] 게임오버·승리 화면이 1920×1080에서 중앙에 정상 표시되는지 확인한다.
- [ ] 낮 종료 확인 팝업이 일반 HUD보다 위에 표시되는지 확인한다.
- [ ] 낮 종료 확인 팝업이 열린 동안 아래 HUD가 입력을 받지 않는지 확인한다.
- [ ] 낮 종료 확인 팝업의 일시정지 필요 여부를 확정하고 동작을 확인한다.

## 8. 관련 파일

- `Assets/Scenes/GameScene.unity` — Canvas 계층과 표준 정렬값의 정본
- `Assets/Personal/SUNJIN/Prefabs/SettingCanvas.prefab` — 설정 모달, `GuardPanel` 및 Canvas 정렬 설정
- `Assets/Scripts/SettingUI/SettingUI.cs` — 설정 화면 표시, 입력 및 설정 일시정지 제어
- `Assets/Scripts/UI/GameSpeedController.cs` — 모달별 게임 일시정지 사유 관리
- `Assets/Scripts/UI/PhasePanelSwitcher.cs` — 낮/밤 하단 패널 활성 전환
- `Assets/Scripts/UI/TowerPanel/TowerTooltipView.cs` — 코드 생성 최상단 툴팁 오버레이
- `Assets/Scripts/Reward/WaveRewardSelectionUI.cs` — 보상 모달 및 보상 일시정지 처리
- `Assets/Scripts/ManagementSpace/UI/ManagementEndDayConfirmPopup.cs` — 낮 종료 확인 모달 제어
- `Assets/Scripts/ManagementSpace/UI/ManagementPanelView.cs` — 관리 패널 내부 자원 행 정렬
- `Docs/Core/SceneWorkflow.md` — 씬 작업 및 정본 반영 절차
