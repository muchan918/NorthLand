# UI 표시 레이어(z-order) 규칙

> 관련 이슈: #188  
> 정본 씬: `Assets/Scenes/GameScene.unity`  
> 상태: 씬 구조 반영 완료, 플레이 모드 대표 겹침 테스트 필요

## 1. 목적

HUD와 모달 UI의 표시 순서를 Canvas 계층의 우연한 배치에 맡기지 않고, 팀 공통 규칙으로 고정한다.
상위 모달이 열린 동안에는 미니맵을 포함한 하위 UI가 포인터 입력을 받지 않아야 한다.

이 문서는 `GameScene` UI를 추가하거나 이동할 때 따라야 하는 표시 순서와 입력 차단 규칙의
단일 기준이다.

## 2. uGUI 적용 원칙

- 서로 다른 루트 Canvas의 우선순위는 `Canvas.sortingOrder`로 결정한다.
- 같은 Canvas 안에서는 sibling 인덱스를 사용한다. Hierarchy에서 뒤 sibling일수록 위에 그려진다.
- 자식 Canvas가 부모와 독립적인 순서를 가져야 할 때만 `overrideSorting`을 켠다.
- 독립 순서가 필요하지 않은 자식 Canvas는 `overrideSorting = false`, `sortingOrder = 0`을 유지한다.
- `GraphicRaycaster`만으로는 빈 화면 영역의 입력을 차단할 수 없다. 모달에는 화면 전체를 덮는
  `Graphic`이 필요하다.

## 3. 표준 Canvas 레이어

아래 표는 낮은 레이어에서 높은 레이어 순서다.

| 우선순위 | Canvas | `sortingOrder` | 용도 |
|---:|---|---:|---|
| 기본 | `UICanvas` | `100` | 일반 HUD, 미니맵, 관리·타워·스킬·정보 패널 |
| 상위 모달 | `RewardCanvas` | `500` | 보상 선택 화면 |
| 최상위 모달 | `ResultCanvas` | `900` | 게임오버·승리 결과 화면 |

새 루트 Canvas를 추가할 때는 임의의 큰 숫자를 사용하지 않는다. 위 세 범주 중 하나에 포함시키고,
새 범주가 꼭 필요하면 이 표를 먼저 갱신한다.

## 4. `UICanvas` 내부 순서

`UICanvas` 내부 UI는 Hierarchy의 sibling 순서를 사용한다. 현재 정본 씬의 아래→위 순서는 다음과
같다.

1. `ManagementCanvas`
2. `TooltipUI`
3. `TowerPanel`
4. `SkillPanel`
5. `BaseHealthBar`
6. `TowerInfoPanel`
7. `BalanceTestPanel`
8. `Minimap`

`Minimap`은 `UICanvas`의 자식 Canvas이며 `overrideSorting = false`, `sortingOrder = 0`이다.
따라서 루트 Canvas처럼 별도 전역 레이어를 차지하지 않고 `UICanvas`의 순서를 따른다.

`BalanceTestPanel`은 개발·밸런스 테스트용 UI이므로 일반 HUD보다 위에 둔다. 출시용 UI를 추가할
때 이 패널을 일반 표시 순서의 기준으로 사용하지 않는다.

## 5. 모달 입력 차단

다음 패널은 활성화될 때 화면 전체 크기의 `Image`가 입력을 받아 하위 UI로의 포인터 입력 통과를
막는다.

| 모달 | 입력 차단 Graphic | 요구 설정 |
|---|---|---|
| 보상 선택 | `Rewardpanel`의 전체 화면 `Image` | `raycastTarget = true` |
| 게임오버 | `GameOverPanel`의 전체 화면 `Image` | `raycastTarget = true` |
| 승리 | `VictoryPanel`의 전체 화면 `Image` | `raycastTarget = true` |

차단용 Image는 투명해도 된다. 단, 다음 조건을 모두 만족해야 한다.

- 앵커가 화면 전체를 덮는다.
- 모달 콘텐츠보다 먼저 그려지도록 모달 루트의 첫 sibling에 둔다.
- `raycastTarget`이 켜져 있다.
- 모달이 닫히면 함께 비활성화된다.

모달 아래에 있는 버튼을 개별적으로 비활성화하는 방식은 사용하지 않는다. 새 하위 UI가 추가될
때 누락될 수 있기 때문이다.

## 6. 변경 절차

1. 씬 작업은 `Docs/Core/SceneWorkflow.md`를 따른다.
2. 최종 결과는 정본 `Assets/Scenes/GameScene.unity`에 반영한다.
3. 새 UI가 일반 HUD인지 모달인지 결정한다.
4. 루트 Canvas가 필요하면 §3의 표준 `sortingOrder`를 적용한다.
5. 일반 HUD라면 `UICanvas` 안에서 §4의 상대 우선순위에 맞게 sibling 위치를 정한다.
6. 모달이라면 §5의 전체 화면 입력 차단 Graphic을 함께 배치한다.
7. 아래 대표 겹침 테스트를 수행한다.

## 7. 검증 체크리스트

- [x] `UICanvas`의 `sortingOrder`가 `100`이다.
- [x] `RewardCanvas`의 `sortingOrder`가 `500`이다.
- [x] `ResultCanvas`의 `sortingOrder`가 `900`이다.
- [x] `Minimap`이 `UICanvas` 아래에 있고 독립 전역 정렬을 사용하지 않는다.
- [x] 보상·게임오버·승리 패널에 전체 화면 raycast 차단 Graphic이 있다.
- [x] 보상 패널이 미니맵과 일반 HUD보다 위에 표시되는지 플레이 모드에서 확인한다.
- [x] 보상 패널이 열린 동안 미니맵 클릭 이동이 발생하지 않는지 확인한다.
- [x] 게임오버·승리 화면이 열린 동안 아래 HUD 버튼이 입력을 받지 않는지 확인한다.

## 8. 관련 파일

- `Assets/Scenes/GameScene.unity` — Canvas 계층과 표준 정렬값의 정본
- `Assets/Scripts/UI/PhasePanelSwitcher.cs` — 낮/밤 하단 패널 활성 전환
- `Assets/Scripts/ManagementSpace/UI/ManagementPanelView.cs` — 관리 패널 내부 자원 행 정렬
- `Docs/Core/SceneWorkflow.md` — 씬 작업 및 정본 반영 절차

